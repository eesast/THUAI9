## Cloud Function API Contract

Client entry points are configured in `CloudFunctionConfig.cs`.

### 1. List files / version manifest

`GET /files`

Recommended response shape:

```json
{
  "version": "f3213d9c...",
  "generatedAt": "2026-05-10T10:00:00Z",
  "files": [
    {
      "key": "game/client.zip",
      "size": 123456789,
      "lastModified": "2026-04-01T12:00:00Z",
      "hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "hashAlgorithm": "sha256"
    }
  ]
}
```

The client also accepts:

- a top-level JSON array instead of `{ "files": [...] }`
- `items` instead of `files`
- `path` or `name` instead of `key`
- `etag` instead of `hash`
- an optional `downloadUrl` or `url` field on each file item

### 2. Sign one file

`GET /sign?key=game%2Fclient.zip`

Accepted response shapes:

```json
{
  "downloadUrl": "https://cos.example.com/..."
}
```

The client also accepts:

- `url` instead of `downloadUrl`
- a top-level JSON string containing the signed URL

### 3. Rebuild manifest

`GET /manifest/rebuild?token=...`

This endpoint is intended for release-time use, not for every client.

- It walks all COS objects under the configured prefix
- Computes a stable hash manifest
- Stores the manifest back into COS

Recommended environment variables:

- `COS_MANIFEST_KEY`
- `MANIFEST_ADMIN_TOKEN`

### Client sync behavior

- Files are downloaded into `Desktop/CosDownloads/` by default; the user can choose another parent folder
- Object keys with `/` create nested folders locally
- The client stores per-download-root manifests under `%LOCALAPPDATA%/THUAI9Downloader/manifests/`
- Legacy `.thuai9-manifest.json` files in the download directory are still accepted on load
- Protected files and directories are stored per download root under `%LOCALAPPDATA%/THUAI9Downloader/protections/` and skipped during sync
- If `hash/hashAlgorithm` are present, unchanged files are skipped
- If hash is missing, the client falls back to remote size / lastModified metadata
- Pausing retains completed files and discards the incomplete temporary file; continuing sync downloads that file again rather than resuming a byte range
- The desktop client only downloads and syncs files; game launch is handled by the player package
