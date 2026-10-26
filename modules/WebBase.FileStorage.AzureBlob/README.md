# WebBase.FileStorage.AzureBlob

A file storage module: **Azure Blob Storage**. The CMS can keep its pages
here (`CMS_SOURCE=azure`, [docs/CMS.md](../../docs/CMS.md)). Built and tested
with the repository, but **not part of the app until you add it**:

```bash
./scripts/module.sh add azure
```

(That's a `<ProjectReference>` in `src/WebBase.Api/WebBase.Api.csproj` and
`new AzureBlobModule()` in `src/WebBase.Api/Modules.cs`. Commit both.)
[docs/MODULES.md](../../docs/MODULES.md) explains modules.

## Settings

For the CMS, under `Cms:Azure`:

| `.env.local` | Setting | Meaning |
|---|---|---|
| `CMS_AZURE_CONTAINER_URL` | `Cms__Azure__ContainerUrl` | `https://<account>.blob.core.windows.net/<container>`. Signs in as the app's managed identity (or your `az login`), which needs **Storage Blob Data Reader** on the container. With a SAS token on the end (`?sv=...&sig=...`, read + list) it uses that instead, and is then **a secret** |
| `CMS_AZURE_CONNECTION_STRING` + `CMS_AZURE_CONTAINER` | `Cms__Azure__ConnectionString`, `__Container` | Instead of the URL: the storage account's connection string. **A secret**, and it can write too; prefer the URL |
| `CMS_AZURE_PREFIX` | `Cms__Azure__Prefix` | A folder in the container, e.g. `www`. Empty = all of it |

## Tests

`tests/WebBase.FileStorage.AzureBlob.Tests/` runs the file store contract
against Azurite, Microsoft's emulator, which `./scripts/test.sh` starts with
a one-off random key (`TEST_AZURE_BLOB`).
