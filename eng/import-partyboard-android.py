"""Import pinned Java glue; never changes the upstream checkout."""
import argparse
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "10673868b69bbcdada496215656a5b7012145f5c"


def replace_method(source, signature, replacement):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[:start] + replacement + source[end:]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-archive", type=Path)
    arguments = parser.parse_args()
    archive = arguments.source_archive
    if archive is None:
        archive = ROOT / "artifacts" / "partyboard" / f"source-{COMMIT}.zip"
        archive.parent.mkdir(parents=True, exist_ok=True)
        if not archive.exists():
            urllib.request.urlretrieve(f"https://codeload.github.com/zeranemesis/Marioparty4/zip/{COMMIT}", archive)
    target = ROOT / "vendor" / "partyboard-android" / "java"
    marker = "platforms/android/app/src/main/java/"
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            if marker not in entry.filename or not entry.filename.endswith(".java"):
                continue
            relative = entry.filename.split(marker, 1)[1]
            if "/quest/" in relative:
                continue  # This integration targets phones, not the separate Quest runtime.
            text = package.read(entry).decode("utf-8-sig")
            changed = False
            if relative.endswith(("OnlineService.java", "RestartActivity.java")):
                text = text.replace("p.processName.equals(getPackageName())", 'p.processName.equals(getPackageName() + ":partyboard")')
                changed = True
            if relative.endswith(("PartyBoardActivity.java", "LobbyActivity.java", "OnlineService.java", "RestartActivity.java")):
                opening = text.index("{", text.index("public ")) + 1
                text = text[:opening] + '''
    @Override public java.io.File getFilesDir() {
        return com.mariopartyrd.partyboard.RuntimePaths.files(this);
    }
''' + text[opening:]
                changed = True
            if relative.endswith("PartyBoardActivity.java"):
                text = "\n".join(line for line in text.splitlines() if not line.startswith("import com.google."))
                start = text.index('        setLink("scanning", "");')
                end = text.index('        return true;', start)
                text = text[:start] + '''        setLink("scanning", "");
        runOnUiThread(() -> {
            android.widget.EditText input = new android.widget.EditText(this);
            input.setHint("CSL1:...");
            new android.app.AlertDialog.Builder(this)
                .setTitle(french ? "Lier CubeShelf" : "Link CubeShelf")
                .setMessage(french ? "Colle le code affiché sous le QR code du PC." : "Paste the code below the PC's QR code.")
                .setView(input)
                .setPositiveButton("OK", (dialog, which) -> runCubeShelfLink(mode, input.getText().toString(), french))
                .setNegativeButton(french ? "Annuler" : "Cancel", (dialog, which) -> setLink("error", french ? "Annulé." : "Cancelled."))
                .setOnCancelListener(dialog -> setLink("error", french ? "Annulé." : "Cancelled."))
                .show();
        });
''' + text[end:]
                text = replace_method(text, "public long getInstalledVersionCode()", '''public long getInstalledVersionCode() {
        // Engine updates are distributed by CubeShelf, never install a standalone APK.
        return Integer.MAX_VALUE;
    }''')
                changed = True
            destination = target / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            if changed:
                text = "// Modified for CubeShelf embedding; see ../UPSTREAM.md.\n" + text
            destination.write_text(text, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
