# WebBase.FileStorage.GoogleCloud

A file storage module: **Google Cloud Storage**. The CMS can keep its pages
here (`CMS_SOURCE=gcs`, [docs/CMS.md](../../docs/CMS.md)); on Cloud Run that
fits in Google's free tier ([docs/GOOGLE_CLOUD.md](../../docs/GOOGLE_CLOUD.md)).
Built and tested with the repository, but **not part of the app until you add it**:

```bash
./scripts/module.sh add gcs
```

(That's a `<ProjectReference>` in `src/WebBase.Api/WebBase.Api.csproj` and
`new GoogleCloudStorageModule()` in `src/WebBase.Api/Modules.cs`. Commit
both.) [docs/MODULES.md](../../docs/MODULES.md) explains modules.

## Settings

For the CMS, under `Cms:Gcs` (in `cloud.env` for Cloud Run, `.env.local` locally):

| Setting file | App setting | Meaning |
|---|---|---|
| `CMS_GCS_BUCKET` | `Cms__Gcs__Bucket` | The bucket. Required. `./scripts/cloud/gcp.sh setup` creates it |
| `CMS_GCS_PREFIX` | `Cms__Gcs__Prefix` | A folder in it, e.g. `www`. Empty = the whole bucket |

**No keys.** It signs in with Google's Application Default Credentials: on
Cloud Run, the service's own account, which `gcp.sh setup` gives
**Storage Object Viewer** on the bucket. On your computer,
`gcloud auth application-default login` (or `GOOGLE_APPLICATION_CREDENTIALS`
pointing at a key file), though for local work the `files` source is simpler.

Publishing pages to it: `gcloud storage rsync content gs://<bucket>/<prefix> --recursive --delete-unmatched-destination-objects`
(Cloud Shell has `gcloud`). They show on the site within a minute.

## Tests

`tests/WebBase.FileStorage.GoogleCloud.Tests/` runs the file store contract
against fake-gcs-server, which `./scripts/test.sh` starts (`TEST_GCS_URL`;
`Cms__Gcs__ServiceUrl` points the store at it, for tests only).
