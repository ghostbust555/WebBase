# WebBase.FileStorage.S3

A file storage module: **Amazon S3**, or anything S3-compatible: Cloudflare
R2, DigitalOcean Spaces, Backblaze B2, MinIO. The CMS can keep its pages
here (`CMS_SOURCE=s3`, [docs/CMS.md](../../docs/CMS.md)). Built and tested with
the repository, but **not part of the app until you add it**:

```bash
./scripts/module.sh add s3
```

(That's a `<ProjectReference>` in `src/WebBase.Api/WebBase.Api.csproj` and
`new S3Module()` in `src/WebBase.Api/Modules.cs`. Commit both.)
[docs/MODULES.md](../../docs/MODULES.md) explains modules.

## Settings

For the CMS, under `Cms:S3` (`.env.local` → compose files):

| `.env.local` | Setting | Meaning |
|---|---|---|
| `CMS_S3_BUCKET` | `Cms__S3__Bucket` | The bucket. Required |
| `CMS_S3_PREFIX` | `Cms__S3__Prefix` | A folder in it, e.g. `www`. Empty = the whole bucket |
| `CMS_S3_REGION` | `Cms__S3__Region` | e.g. `eu-west-1` |
| `CMS_S3_SERVICE_URL` | `Cms__S3__ServiceUrl` | Not AWS? The provider's endpoint, e.g. `https://<account>.r2.cloudflarestorage.com` |
| `CMS_S3_ACCESS_KEY`, `CMS_S3_SECRET_KEY` | `Cms__S3__AccessKey`, `__SecretKey` | Optional, **secrets**. Without them, the standard AWS credentials: the ECS task's role, `AWS_*` variables, `~/.aws` |

Read-only access is enough: `s3:ListBucket` on the bucket, `s3:GetObject` on its objects.

## Tests

`tests/WebBase.FileStorage.S3.Tests/` runs the file store contract
(`tests/WebBase.Testing/FileStoreContractTests.cs`) against S3Mock, which
`./scripts/test.sh` starts (`TEST_S3_URL`).
