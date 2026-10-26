#!/usr/bin/env bash
# Run prod on Amazon Web Services. The step-by-step guide: docs/AWS.md.
#
#   ./scripts/cloud/aws.sh setup     create (or update) everything and deploy; safe to repeat.
#                                    Run it again after changing a setting below.
#   ./scripts/cloud/aws.sh deploy    build what's here and roll it out
#   ./scripts/cloud/aws.sh url       the site's address
#   ./scripts/cloud/aws.sh logs      the app's recent output
#   ./scripts/cloud/aws.sh destroy   DELETE what this created
#
# Needs the AWS CLI v2 (logged in: aws configure, or aws sso login) and Docker.
# AWS CloudShell (the >_ button in the AWS console) has both: nothing to install.
#
# What gets made, set by AWS_TIER:
#   free      (default) AWS Lambda running the same container, with a public HTTPS address
#             (a "function URL"). Lambda's free allowance (1 million requests and 400,000
#             GB-seconds a month) never expires. AWS has no free PostgreSQL that lasts, so
#             the database is yours: a free Neon one (https://neon.tech), as
#             AWS_DATABASE_URL. The first visit after a quiet spell takes a few seconds.
#   standard  Amazon ECS Express Mode: always-on containers behind a load balancer (about
#             $25-40/month before the database) and Amazon RDS for PostgreSQL, private to
#             your VPC (about $15/month for the smallest, more with traffic). AWS_DATABASE_URL
#             is optional here: without it, the RDS database is created.
# Either way, the image goes to a private ECR repository (cents), secrets to Systems
# Manager Parameter Store (free), and CMS_SOURCE=s3 makes a private S3 bucket for the
# pages (cents) that only the app can read.
#
# Settings (environment variables, or lines in .env.local; none are required):
#   AWS_TIER           free or standard                                  default free
#   AWS_REGION         region                                            default us-east-1
#   AWS_DATABASE_URL   postgres://user:password@host:5432/db?sslmode=require   your own database.
#                      Asked for when free needs one; kept in Parameter Store after that
#   AWS_DB_CLASS       RDS size, standard only                           default db.t4g.micro
#   AWS_MAX_CONCURRENCY  most copies of the function running at once (free): the cap on what
#                      a flood can cost                                  default 20
#   APP_NAME, GOOGLE_CLIENT_ID, CMS_ENABLED, CMS_SOURCE, CMS_S3_BUCKET...   the app's own
#                      settings. Keep them in cloud.env (committed): every deploy applies them.
. "$(dirname "$0")/common.sh"

case "${1:-}" in
  setup|deploy|url|logs|destroy) ;;
  *) sed -n '2,37p' "$0"; exit 1 ;;
esac
need_cli aws "Install AWS CLI v2 (https://aws.amazon.com/cli/), or run this in AWS CloudShell."
aws sts get-caller-identity >/dev/null 2>&1 || die "Not logged in to AWS. Run: aws configure  (or aws sso login)"

TIER="$(tier AWS_TIER)"
REGION="$(setting AWS_REGION us-east-1)"
export AWS_REGION="$REGION" AWS_PAGER=""
ACCOUNT="$(aws sts get-caller-identity --query Account --output text)"
FUNCTION="${SLUG}"
SERVICE="${SLUG}"
REPO="${SLUG}"
REGISTRY="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"
LAMBDA_ROLE="${SLUG}-lambda"
EXEC_ROLE="${SLUG}-ecs-execution"
INFRA_ROLE="${SLUG}-ecs-infrastructure"
TASK_ROLE="${SLUG}-ecs-task"
DB_INSTANCE="${SLUG}-db"
DB_SG="${SLUG}-db"
SSM="/${SLUG}"

# With CMS_SOURCE=s3, the pages are read from this region unless told otherwise.
if [ "$(setting CMS_SOURCE)" = s3 ] && [ -z "$(setting CMS_S3_REGION)" ]; then export CMS_S3_REGION="$REGION"; fi

exists() { "$@" >/dev/null 2>&1; }

# A newly created role takes a moment to be usable everywhere in AWS.
retry() {
  local n
  for n in 1 2 3 4 5 6 7 8; do "$@" && return 0; [ "$n" = 8 ] || sleep 10; done
  return 1
}

# Text made safe to put inside a JSON string.
json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

ssm_get() { aws ssm get-parameter --name "${SSM}/$1" --with-decryption --query Parameter.Value --output text 2>/dev/null || true; }
ssm_put() { aws ssm put-parameter --name "${SSM}/$1" --type SecureString --value "$2" --overwrite >/dev/null; }

role_trust() { printf '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"%s"},"Action":"sts:AssumeRole"}]}' "$1"; }

ensure_role() { # name, service principal, managed policy (optional)
  if ! exists aws iam get-role --role-name "$1"; then
    aws iam create-role --role-name "$1" --assume-role-policy-document "$(role_trust "$2")" >/dev/null
    CREATED_ROLE=1
  fi
  if [ -n "${3:-}" ]; then aws iam attach-role-policy --role-name "$1" --policy-arn "$3"; fi
}

# ---- the image --------------------------------------------------------------

ensure_repository() {
  exists aws ecr describe-repositories --repository-names "$REPO" || aws ecr create-repository --repository-name "$REPO" >/dev/null
  # Keep the last 3 images; older ones are deleted, so they don't add up.
  aws ecr put-lifecycle-policy --repository-name "$REPO" --lifecycle-policy-text \
    '{"rules":[{"rulePriority":1,"description":"keep the last 3","selection":{"tagStatus":"any","countType":"imageCountMoreThan","countNumber":3},"action":{"type":"expire"}}]}' >/dev/null
}

# Builds the image and pushes it. Sets BUILD and IMAGE.
build_image() {
  can_build || die "Building needs Docker. AWS CloudShell has it; or install Docker Desktop."
  ensure_repository
  aws ecr get-login-password | docker login --username AWS --password-stdin "$REGISTRY" >/dev/null
  BUILD="$(build_id)"
  IMAGE="${REGISTRY}/${REPO}:${BUILD}"
  if [ "$TIER" = free ]; then
    # Lambda wants the app image plus its adapter (ops/Dockerfile.lambda), and
    # a plain image manifest, without the extra records newer Docker adds.
    say "Building the image for Lambda (linux/amd64)"
    docker build --platform linux/amd64 -f ops/Dockerfile --build-arg "BUILD_ID=${BUILD}" -t "${REPO}-base:${BUILD}" .
    docker build --platform linux/amd64 --provenance=false -f ops/Dockerfile.lambda \
      --build-arg "BASE=${REPO}-base:${BUILD}" -t "$IMAGE" .
    say "Uploading it"
    docker push "$IMAGE"
  else
    build_and_push "$IMAGE" "$BUILD"
  fi
}

# ---- the CMS bucket ---------------------------------------------------------

# A private S3 bucket for the CMS's pages. Prints nothing; sets CMS_POLICY to
# the JSON that lets the app (and only it) read the bucket, or leaves it empty.
CMS_POLICY=""
setup_cms_bucket() {
  [ "$(setting CMS_SOURCE)" = s3 ] || return 0
  local bucket; bucket="$(setting CMS_S3_BUCKET)"
  [ -n "$bucket" ] || die "CMS_SOURCE is s3, so set CMS_S3_BUCKET to a bucket name (unique across all of AWS)."
  need_module s3 WebBase.FileStorage.S3 "CMS_SOURCE is s3"
  if [ -n "$(setting CMS_S3_SERVICE_URL)" ]; then warn "CMS_S3_SERVICE_URL is set, so the pages are somewhere else: no bucket is created."; return 0; fi
  say "S3 bucket ${bucket} for the CMS"
  if ! exists aws s3api head-bucket --bucket "$bucket"; then
    if [ "$REGION" = us-east-1 ]; then
      aws s3api create-bucket --bucket "$bucket" >/dev/null
    else
      aws s3api create-bucket --bucket "$bucket" --create-bucket-configuration "LocationConstraint=${REGION}" >/dev/null
    fi
  fi
  aws s3api put-public-access-block --bucket "$bucket" \
    --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
  CMS_POLICY="{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Action\":\"s3:ListBucket\",\"Resource\":\"arn:aws:s3:::${bucket}\"},{\"Effect\":\"Allow\",\"Action\":\"s3:GetObject\",\"Resource\":\"arn:aws:s3:::${bucket}/*\"}]}"
}

# Lets a role read the CMS bucket, or take that permission away again.
apply_cms_policy() { # role
  if [ -n "$CMS_POLICY" ]; then
    aws iam put-role-policy --role-name "$1" --policy-name "${SLUG}-cms-bucket" --policy-document "$CMS_POLICY"
  else
    aws iam delete-role-policy --role-name "$1" --policy-name "${SLUG}-cms-bucket" >/dev/null 2>&1 || true
  fi
}

# ---- the database -----------------------------------------------------------

# An RDS PostgreSQL in the default VPC, which is where ECS Express Mode runs
# the app. Not publicly reachable: its firewall (security group) lets in
# PostgreSQL from inside that VPC only. Prints the database's URL.
ensure_rds() {
  local vpc cidr sg host password
  vpc="$(aws ec2 describe-vpcs --filters Name=is-default,Values=true --query 'Vpcs[0].VpcId' --output text)"
  [ -n "$vpc" ] && [ "$vpc" != None ] || die "This region has no default VPC. Create one (VPC console → Actions → Create default VPC), or set AWS_DATABASE_URL."
  cidr="$(aws ec2 describe-vpcs --vpc-ids "$vpc" --query 'Vpcs[0].CidrBlock' --output text)"

  sg="$(aws ec2 describe-security-groups --filters "Name=vpc-id,Values=${vpc}" "Name=group-name,Values=${DB_SG}" \
        --query 'SecurityGroups[0].GroupId' --output text)"
  if [ -z "$sg" ] || [ "$sg" = None ]; then
    sg="$(aws ec2 create-security-group --group-name "$DB_SG" --vpc-id "$vpc" \
          --description "PostgreSQL for ${SLUG}, from inside the VPC only" --query GroupId --output text)"
    aws ec2 authorize-security-group-ingress --group-id "$sg" --protocol tcp --port 5432 --cidr "$cidr" >/dev/null
  fi

  password="$(random_secret 32)"
  if exists aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE"; then
    # Made by an earlier run whose password wasn't kept: set a new one.
    aws rds modify-db-instance --db-instance-identifier "$DB_INSTANCE" --master-user-password "$password" --apply-immediately >/dev/null
  else
    aws rds create-db-instance --db-instance-identifier "$DB_INSTANCE" \
      --engine postgres --db-instance-class "$(setting AWS_DB_CLASS db.t4g.micro)" \
      --allocated-storage 20 --storage-type gp3 --storage-encrypted \
      --master-username "${SLUG}admin" --master-user-password "$password" \
      --db-name "$SLUG" --vpc-security-group-ids "$sg" --no-publicly-accessible \
      --backup-retention-period 7 --auto-minor-version-upgrade >/dev/null
  fi
  aws rds wait db-instance-available --db-instance-identifier "$DB_INSTANCE"
  host="$(aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE" --query 'DBInstances[0].Endpoint.Address' --output text)"
  echo "postgres://${SLUG}admin:${password}@${host}:5432/${SLUG}?sslmode=require"
}

# Decides the database and keeps its URL in Parameter Store.
setup_database() {
  local url; url="$(setting AWS_DATABASE_URL)"
  if [ -n "$url" ]; then
    ssm_put database-url "$url"
  elif [ -n "$(ssm_get database-url)" ]; then
    ok "Database: keeping the one already set up"
  elif [ "$TIER" = standard ]; then
    say "PostgreSQL on Amazon RDS, ${DB_INSTANCE} (the slowest step: 5-15 minutes)"
    ssm_put database-url "$(ensure_rds)"
  elif [ -t 0 ]; then
    echo
    bold "The app needs a PostgreSQL database."
    echo "  Free: create one at https://neon.tech (or https://supabase.com) and copy its"
    echo "  connection string, postgres://... It's kept in Parameter Store, not shown here."
    read -r -s -p "  Connection string: " url; echo
    [ -n "$url" ] || die "No database. Run again with AWS_DATABASE_URL=postgres://..."
    ssm_put database-url "$url"
  else
    die "No database. Set AWS_DATABASE_URL=postgres://... (a free one: https://neon.tech) and run setup again."
  fi
  case "$(ssm_get database-url)" in postgres://*|postgresql://*) ;; *) die "The database setting doesn't start with postgres://. Run setup again with AWS_DATABASE_URL=postgres://..." ;; esac
  # The key that signs sign-ins: made once, then kept.
  [ -n "$(ssm_get jwt-key)" ] || ssm_put jwt-key "$(random_secret 64)"
}

# ---- free: Lambda -----------------------------------------------------------

function_url() { aws lambda get-function-url-config --function-name "$FUNCTION" --query FunctionUrl --output text 2>/dev/null | sed 's#/$##' || true; }

lambda_env_json() {
  local line first=1 name value
  printf '{"Variables":{'
  { app_settings; echo "Jwt__Key=$(ssm_get jwt-key)"; echo "DATABASE_URL=$(ssm_get database-url)"; } | while IFS= read -r line; do
    name="${line%%=*}"; value="${line#*=}"
    printf '%s"%s":"%s"' "$([ "$first" = 1 ] || echo ,)" "$name" "$(json_escape "$value")"
    first=0
  done
  printf '}}'
}

lambda_wait() { aws lambda wait function-updated-v2 --function-name "$FUNCTION"; }

lambda_deploy() { # image, create-if-missing
  if exists aws lambda get-function --function-name "$FUNCTION"; then
    say "Updating the function"
    aws lambda update-function-code --function-name "$FUNCTION" --image-uri "$1" >/dev/null
    lambda_wait
    aws lambda update-function-configuration --function-name "$FUNCTION" --environment "$(lambda_env_json)" \
      --memory-size 1024 --timeout 30 >/dev/null
    lambda_wait
  else
    say "Creating the function (1 GB, 30 s)"
    retry aws lambda create-function --function-name "$FUNCTION" --package-type Image --code "ImageUri=$1" \
      --role "arn:aws:iam::${ACCOUNT}:role/${LAMBDA_ROLE}" --architectures x86_64 --memory-size 1024 --timeout 30 \
      --environment "$(lambda_env_json)" >/dev/null \
      || die "AWS didn't accept the function. Run this again in a minute: new roles take a while to be usable."
    aws lambda wait function-active-v2 --function-name "$FUNCTION"
  fi
  if [ -z "$(function_url)" ] || [ "$(function_url)" = None ]; then
    say "A public HTTPS address for it"
    aws lambda create-function-url-config --function-name "$FUNCTION" --auth-type NONE >/dev/null
  fi
  # The cap on what a flood can cost: at most this many copies run at once.
  # (A new account may be allowed no reserved concurrency yet; that's not fatal.)
  aws lambda put-function-concurrency --function-name "$FUNCTION" \
    --reserved-concurrent-executions "$(setting AWS_MAX_CONCURRENCY 20)" >/dev/null 2>&1 \
    || warn "Couldn't cap the function's concurrency (new AWS accounts often can't yet). Set a budget alert; the app still bans addresses that send too many requests."
  # Public, as a website must be. AWS now wants both permissions for this.
  aws lambda add-permission --function-name "$FUNCTION" --statement-id public-url --action lambda:InvokeFunctionUrl \
    --principal '*' --function-url-auth-type NONE >/dev/null 2>&1 || true
  aws lambda add-permission --function-name "$FUNCTION" --statement-id public-invoke --action lambda:InvokeFunction \
    --principal '*' --invoked-via-function-url >/dev/null 2>&1 || true
}

# ---- standard: ECS Express Mode ---------------------------------------------

container_json() {
  local image="$1" line out=""
  while IFS= read -r line; do
    out+=",
    {\"name\": \"${line%%=*}\", \"value\": \"$(json_escape "${line#*=}")\"}"
  done < <(app_settings | grep -v '^App__Environment=')
  cat <<JSON
{
  "image": "${image}",
  "containerPort": 8080,
  "environment": [
    {"name": "App__Environment", "value": "prod"}${out}
  ],
  "secrets": [
    {"name": "Jwt__Key", "valueFrom": "arn:aws:ssm:${REGION}:${ACCOUNT}:parameter${SSM}/jwt-key"},
    {"name": "DATABASE_URL", "valueFrom": "arn:aws:ssm:${REGION}:${ACCOUNT}:parameter${SSM}/database-url"}
  ]
}
JSON
}

service_arn() {
  local arn; arn="$(ssm_get service-arn)"
  [ -n "$arn" ] && [ "$arn" != None ] || die "No service yet. Run ./scripts/cloud/aws.sh setup first."
  echo "$arn"
}

service_url() {
  local endpoint
  endpoint="$(aws ecs describe-express-gateway-service --service-arn "$(service_arn)" --output json \
    | grep -oE '"endpoint": *"[^"]+"' | head -1 | sed -E 's/.*"([^"]+)"$/\1/')"
  case "$endpoint" in
    "") echo "" ;;
    http*) echo "$endpoint" ;;
    *) echo "https://${endpoint}" ;;
  esac
}

# Where the site is, whichever tier made it.
site_url() {
  if exists aws lambda get-function --function-name "$FUNCTION"; then function_url
  elif [ -n "$(ssm_get service-arn)" ]; then service_url
  fi
}

check_health() {
  local url; url="$(site_url)"
  [ -n "$url" ] || die "No address yet. Try ./scripts/cloud/aws.sh url in a minute."
  say "Checking ${url}/api/health (the first answer after a quiet spell is slow)"
  wait_for_url "${url}/api/health" 150 || die "The new version doesn't answer. See why: ./scripts/cloud/aws.sh logs"
  ok "It works: ${url}"
}

case "$1" in
  setup)
    say "Account ${ACCOUNT}, region ${REGION}. Tier: ${TIER}"
    if [ "$TIER" = free ] && [ -z "$(setting AWS_DATABASE_URL)" ] && [ -z "$(ssm_get database-url)" ] && [ ! -t 0 ]; then
      die "The free tier needs a database you bring: a free one at https://neon.tech. Run again with AWS_DATABASE_URL=postgres://..."
    fi
    CREATED_ROLE=0
    setup_cms_bucket
    setup_database
    cms_secrets_notice

    if [ "$TIER" = free ]; then
      say "Permissions (an IAM role the function runs as)"
      ensure_role "$LAMBDA_ROLE" lambda.amazonaws.com arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole
      apply_cms_policy "$LAMBDA_ROLE"
      [ "$CREATED_ROLE" = 1 ] && { say "Waiting a minute for the new role to take effect"; sleep 60; }
      build_image
      lambda_deploy "$IMAGE"
    else
      say "Permissions (IAM roles)"
      ensure_role "$EXEC_ROLE" ecs-tasks.amazonaws.com arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy
      ensure_role "$INFRA_ROLE" ecs.amazonaws.com arn:aws:iam::aws:policy/service-role/AmazonECSInfrastructureRoleforExpressGatewayServices
      ensure_role "$TASK_ROLE" ecs-tasks.amazonaws.com
      # The containers may read this project's secrets, and nothing else.
      aws iam put-role-policy --role-name "$EXEC_ROLE" --policy-name "${SLUG}-read-secrets" --policy-document \
        "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Action\":\"ssm:GetParameters\",\"Resource\":\"arn:aws:ssm:${REGION}:${ACCOUNT}:parameter${SSM}/*\"}]}"
      apply_cms_policy "$TASK_ROLE"
      [ "$CREATED_ROLE" = 1 ] && { say "Waiting a minute for the new roles to take effect"; sleep 60; }
      build_image
      if [ -n "$(ssm_get service-arn)" ]; then
        say "Updating the service"
        aws ecs update-express-gateway-service --service-arn "$(service_arn)" \
          --primary-container "$(container_json "$IMAGE")" --output json >/dev/null
      else
        say "Creating the service (several minutes: it builds a load balancer and certificate)"
        OUTPUT="$(aws ecs create-express-gateway-service --output json \
          --service-name "$SERVICE" \
          --execution-role-arn "arn:aws:iam::${ACCOUNT}:role/${EXEC_ROLE}" \
          --infrastructure-role-arn "arn:aws:iam::${ACCOUNT}:role/${INFRA_ROLE}" \
          --task-role-arn "arn:aws:iam::${ACCOUNT}:role/${TASK_ROLE}" \
          --primary-container "$(container_json "$IMAGE")" \
          --health-check-path /api/health --cpu 512 --memory 1024 \
          --scaling-target '{"minTaskCount":1,"maxTaskCount":3}')"
        ARN="$(printf '%s' "$OUTPUT" | grep -oE '"serviceArn": *"[^"]+"' | head -1 | sed -E 's/.*"([^"]+)"$/\1/')"
        [ -n "$ARN" ] || die "AWS did not return a service. Its answer: ${OUTPUT:0:500}"
        ssm_put service-arn "$ARN"
      fi
      aws ecs monitor-express-gateway-service --service-arn "$(service_arn)" || true
    fi
    check_health
    if [ "$TIER" = free ]; then
      echo "    Free tier: the app sleeps when idle, so the first visit after a quiet spell takes a few seconds."
      echo "    Set a budget alert: https://console.aws.amazon.com/billing/home#/budgets"
    fi
    echo "    Next time, just: ./scripts/cloud/aws.sh deploy"
    ;;

  deploy)
    if exists aws lambda get-function --function-name "$FUNCTION"; then
      TIER=free
      build_image
      say "Rolling out ${BUILD}"
      lambda_deploy "$IMAGE"
    elif [ -n "$(ssm_get service-arn)" ]; then
      TIER=standard
      build_image
      say "Rolling out ${BUILD} (AWS shifts traffic gradually and rolls back on errors)"
      aws ecs update-express-gateway-service --service-arn "$(service_arn)" \
        --primary-container "$(container_json "$IMAGE")" --output json >/dev/null
      aws ecs monitor-express-gateway-service --service-arn "$(service_arn)" || true
    else
      die "Nothing set up yet. Run ./scripts/cloud/aws.sh setup first."
    fi
    check_health
    ;;

  url) site_url ;;

  logs)
    if exists aws lambda get-function --function-name "$FUNCTION"; then
      aws logs tail "/aws/lambda/${FUNCTION}" --since 1h --follow
    else
      aws logs tail "/aws/ecs/${SERVICE}" --since 1h --follow 2>/dev/null \
        || die "Find the service's logs in the CloudWatch console, under Log groups."
    fi
    ;;

  destroy)
    OURS_DB=0
    warn "This deletes the app (function or service), its image repository and the stored secrets."
    if exists aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE"; then
      OURS_DB=1
      warn "And the RDS database ${DB_INSTANCE}. A final snapshot of it is kept (${DB_INSTANCE}-final-...),"
      warn "which you can restore from or delete in the RDS console (Snapshots)."
    else
      warn "Your own database (AWS_DATABASE_URL) is left alone. A CMS bucket is left alone too."
    fi
    confirm "Really delete them?" || exit 1
    if exists aws lambda get-function --function-name "$FUNCTION"; then
      aws lambda delete-function-url-config --function-name "$FUNCTION" >/dev/null 2>&1 || true
      aws lambda delete-function --function-name "$FUNCTION" >/dev/null || true
    fi
    if [ -n "$(ssm_get service-arn)" ]; then
      aws ecs delete-express-gateway-service --service-arn "$(service_arn)" >/dev/null || true
    fi
    if [ "$OURS_DB" = 1 ]; then
      aws rds delete-db-instance --db-instance-identifier "$DB_INSTANCE" \
        --final-db-snapshot-identifier "${DB_INSTANCE}-final-$(date +%Y%m%d-%H%M%S)" >/dev/null || true
      say "Waiting for the database to be deleted (several minutes)"
      aws rds wait db-instance-deleted --db-instance-identifier "$DB_INSTANCE" || true
      SG_ID="$(aws ec2 describe-security-groups --filters "Name=group-name,Values=${DB_SG}" --query 'SecurityGroups[0].GroupId' --output text 2>/dev/null)"
      if [ -n "$SG_ID" ] && [ "$SG_ID" != None ]; then aws ec2 delete-security-group --group-id "$SG_ID" || true; fi
    fi
    aws ecr delete-repository --repository-name "$REPO" --force >/dev/null || true
    aws ssm delete-parameters --names "${SSM}/jwt-key" "${SSM}/database-url" "${SSM}/service-arn" >/dev/null || true
    ok "Deleted. (The IAM roles are kept; they cost nothing.)"
    ;;
esac
