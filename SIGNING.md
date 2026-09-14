# Signature Windows

Le workflow `.github/workflows/release.yml` signe `CubeShelf.exe`, `CubeShelf.Updater.exe` et `CubeShelf-Setup-x64.exe` avec SHA-256 et un horodatage RFC 3161.

Secrets attendus :

- `CUBESHELF_SIGN_PFX_BASE64` : certificat `.pfx` encodé en Base64 ;
- `CUBESHELF_SIGN_PFX_PASSWORD` : mot de passe du certificat.

Une release stable déclenchée par tag échoue si le certificat est absent. Le déclenchement manuel permet une compilation non signée uniquement lorsque l’option de dérogation est activée explicitement. Les sommes SHA-256 de tous les artefacts sont publiées dans `checksums.txt`.
