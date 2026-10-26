#!/usr/bin/env bash
# Run prod on Amazon Web Services with Amazon ECS Express Mode: one command
# makes the container service, an HTTPS load balancer, a public URL and auto
# scaling. (AWS App Runner, the older option, closed to new customers in 2026.)
#
#   ./scripts/cloud/aws.sh setup     one-time: create everything and deploy
#   ./scripts/cloud/aws.sh deploy    build what's here now and roll it out
#   ./scripts/cloud/aws.sh url       the site's address
#   ./scripts/cloud/aws.sh destroy   DELETE what this created
#
# Needs the AWS CLI v2, recent enough to have `aws ecs create-express-gateway-service`
# (https://aws.amazon.com/cli/), logged in: aws configure  (or aws sso login)
#
# The database is NOT created by this script - you bring one and give its
# address in AWS_DATABASE_URL. docs/CLOUD.md shows how to make one on Amazon
# RDS, or use any hosted Postgres (Neon, Supabase...).
#
# Settings (environment variables or lines in .env.local):
#   AWS_DATABASE_URL   postgres://user:password@host:5432/dbname?sslmode=require   (required)
#   AWS_REGION         region                                                      default us-east-1
. "$(dirname "$0")/common.sh"
need_docker
need_cli aws "Install AWS CLI v2: https://aws.amazon.com/cli/"

REGION="$(setting AWS_REGION us-east-1)"
export AWS_REGION="$REGION" AWS_PAGER=""
SERVICE="${SLUG}"
EXEC_ROLE="${SLUG}-ecs-execution"
INFRA_ROLE="${SLUG}-ecs-infrastructure"

account() { aws sts get-caller-identity --query Account --output text; }

# Text made safe to put inside a JSON string.
json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

container_json() {
  local image="$1" acct; acct="$(account)"
  local ssm="arn:aws:ssm:${REGION}:${acct}:parameter/${SLUG}"
  cat <<JSON
{
  "image": "${image}",
  "containerPort": 8080,
  "environment": [
    {"name": "App__Environment", "value": "prod"},
    {"name": "App__Name", "value": "$(json_escape "$(setting APP_NAME WebBase)")"},
    {"name": "Auth__Google__ClientId", "value": "$(json_escape "$(setting GOOGLE_CLIENT_ID)")"},
    {"name": "Cms__Enabled", "value": "$(json_escape "$(setting CMS_ENABLED false)")"}
  ],
  "secrets": [
    {"name": "Jwt__Key", "valueFrom": "${ssm}/jwt-key"},
    {"name": "DATABASE_URL", "valueFrom": "${ssm}/database-url"}
  ]
}
JSON
}

registry() { echo "$(account).dkr.ecr.${REGION}.amazonaws.com/${SLUG}"; }

push() {
  local reg; reg="$(registry)"
  aws ecr describe-repositories --repository-names "$SLUG" >/dev/null 2>&1 \
    || aws ecr create-repository --repository-name "$SLUG" >/dev/null
  aws ecr get-login-password | docker login --username AWS --password-stdin "${reg%/*}" >/dev/null
  BUILD="$(build_id)"
  IMAGE="${reg}:${BUILD}"
  build_and_push "$IMAGE" "$BUILD"
}

ensure_role() { # name, service principal, managed policy
  if ! aws iam get-role --role-name "$1" >/dev/null 2>&1; then
    aws iam create-role --role-name "$1" --assume-role-policy-document \
      "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":{\"Service\":\"$2\"},\"Action\":\"sts:AssumeRole\"}]}" >/dev/null
    CREATED_ROLE=1
  fi
  aws iam attach-role-policy --role-name "$1" --policy-arn "$3"
}

service_arn() {
  local arn; arn="$(setting AWS_SERVICE_ARN)"
  [ -n "$arn" ] || die "No service yet. Run ./scripts/cloud/aws.sh setup first."
  echo "$arn"
}

print_url() {
  local endpoint
  endpoint="$(aws ecs describe-express-gateway-service --service-arn "$(service_arn)" --output json \
    | grep -oE '"endpoint": *"[^"]+"' | head -1 | sed -E 's/.*"([^"]+)"$/\1/')"
  case "$endpoint" in
    "") echo "(not ready yet - try again in a minute)" ;;
    http*) echo "$endpoint" ;;
    *) echo "https://${endpoint}" ;;
  esac
}

case "${1:-}" in
  setup)
    need_secrets
    DB_URL="$(setting AWS_DATABASE_URL)"
    [ -n "$DB_URL" ] || die "Set AWS_DATABASE_URL in .env.local first (see docs/CLOUD.md, 'A database on AWS')."
    aws sts get-caller-identity >/dev/null 2>&1 || die "Not logged in to AWS. Run: aws configure  (or aws sso login)"
    say "Account $(account), region ${REGION}"

    say "Secrets (SSM Parameter Store, encrypted)"
    aws ssm put-parameter --name "/${SLUG}/jwt-key" --type SecureString --value "$(setting JWT_KEY)" --overwrite >/dev/null
    aws ssm put-parameter --name "/${SLUG}/database-url" --type SecureString --value "$DB_URL" --overwrite >/dev/null

    say "Permissions (IAM roles)"
    CREATED_ROLE=0
    ensure_role "$EXEC_ROLE" ecs-tasks.amazonaws.com arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy
    ensure_role "$INFRA_ROLE" ecs.amazonaws.com arn:aws:iam::aws:policy/service-role/AmazonECSInfrastructureRoleforExpressGatewayServices
    # Let the containers read this project's secrets, and nothing else.
    aws iam put-role-policy --role-name "$EXEC_ROLE" --policy-name "${SLUG}-read-secrets" --policy-document \
      "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Action\":\"ssm:GetParameters\",\"Resource\":\"arn:aws:ssm:${REGION}:$(account):parameter/${SLUG}/*\"}]}"
    # New roles take a little while to be usable everywhere in AWS.
    [ "$CREATED_ROLE" = 1 ] && { say "Waiting a minute for the new roles to take effect"; sleep 60; }

    push

    say "Creating the service (several minutes: it builds a load balancer and certificate)"
    OUTPUT="$(aws ecs create-express-gateway-service --output json \
      --service-name "$SERVICE" \
      --execution-role-arn "arn:aws:iam::$(account):role/${EXEC_ROLE}" \
      --infrastructure-role-arn "arn:aws:iam::$(account):role/${INFRA_ROLE}" \
      --primary-container "$(container_json "$IMAGE")" \
      --health-check-path /api/health \
      --scaling-target '{"minTaskCount":1,"maxTaskCount":3}')"
    ARN="$(printf '%s' "$OUTPUT" | grep -oE '"serviceArn": *"[^"]+"' | head -1 | sed -E 's/.*"([^"]+)"$/\1/')"
    [ -n "$ARN" ] || die "AWS did not return a service. Its answer: ${OUTPUT:0:500}"
    remember AWS_SERVICE_ARN "$ARN"
    aws ecs monitor-express-gateway-service --service-arn "$ARN" || true
    ok "Deployed. Your site: $(print_url)"
    echo "    Next time, just: ./scripts/cloud/aws.sh deploy"
    ;;

  deploy)
    ARN="$(service_arn)"
    push
    say "Rolling out ${BUILD} (AWS shifts traffic gradually and rolls back on errors)"
    aws ecs update-express-gateway-service --service-arn "$ARN" \
      --primary-container "$(container_json "$IMAGE")" --output json >/dev/null
    aws ecs monitor-express-gateway-service --service-arn "$ARN" || true
    ok "Deployed ${BUILD}: $(print_url)"
    ;;

  url)
    print_url ;;
  destroy)
    warn "This deletes the service, its load balancer, the image repository and the stored secrets."
    warn "Your database is yours and is left alone."
    confirm "Really delete them?" || exit 1
    aws ecs delete-express-gateway-service --service-arn "$(service_arn)" >/dev/null || true
    aws ecr delete-repository --repository-name "$SLUG" --force >/dev/null || true
    aws ssm delete-parameters --names "/${SLUG}/jwt-key" "/${SLUG}/database-url" >/dev/null || true
    remember AWS_SERVICE_ARN ""
    ok "Deleted. (The two IAM roles are kept; they cost nothing.)"
    ;;
  *)
    sed -n '2,20p' "$0"; exit 1 ;;
esac
