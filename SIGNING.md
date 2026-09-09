# CubeShelf Windows code signing

CubeShelf can sign its Windows binaries automatically in GitHub Actions when a code-signing certificate is available.

Repository secrets expected by `.github/workflows/release.yml`:

- `CUBESHELF_SIGN_PFX_BASE64`: Base64 content of the `.pfx` certificate.
- `CUBESHELF_SIGN_PFX_PASSWORD`: password of the `.pfx` certificate.

If these secrets are absent, the workflow still builds and publishes the release, but the executables remain unsigned.

The workflow signs both:

- `CubeShelf.exe`
- `CubeShelf.Updater.exe`

with SHA-256 and an RFC3161 timestamp before creating `CubeShelf-win-x64.zip`.
