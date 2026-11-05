"""Observe official repositories without forking, building or executing their code.

Writes a report and proposed desktop catalogues outside the shipped catalogues.
Proposals need compatibility review before they are shipped; Android always needs
its pinned host adapters regenerated and the application rebuilt.
"""
import argparse
import concurrent.futures
import copy
import datetime
import json
from pathlib import Path
import re
import subprocess
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")


def pattern(name, tag):
    token = tag.lstrip("vV")
    if not token or name.count(token) != 1:
        return name
    prefix, suffix = name.split(token)
    return prefix + "*" + re.sub(r"^\+g[0-9a-fA-F]{7,40}(?=[-_.])", "", suffix)


def matches(name, expression):
    return bool(re.fullmatch(re.escape(expression).replace(r"\*", ".*"), name, re.I))


def candidates(port, release):
    if release.get("draft") or release.get("prerelease"):
        return []
    result = {}
    for known in port.get("Assets", []):
        found = [a for a in release.get("assets", []) if matches(a.get("name", ""), pattern(known["Name"], port.get("ReleaseTag", "")))]
        if len(found) != 1:
            continue
        asset = found[0]
        digest = asset.get("digest") or ""
        host = port.get("RepositoryHost", "github.com")
        prefix = ("https://gitlab.com/api/v4/projects/" + urllib.parse.quote(port["Repository"], safe="") + "/uploads/"
            if host == "gitlab.com" else "https://github.com/" + port["Repository"] + "/releases/download/")
        name = asset.get("name", "")
        if not re.fullmatch(r"sha256:[0-9a-fA-F]{64}", digest) or not 0 < asset.get("size", 0) <= 2 * 1024**3:
            continue
        if not asset.get("browser_download_url", "").startswith(prefix) or not name or re.search(r"[/\\\x00]", name) or name in (".", ".."):
            continue
        if host not in ("github.com", "gitlab.com"):
            continue
        if host == "gitlab.com" and not re.fullmatch(r"[a-fA-F0-9]{32}/" + re.escape(urllib.parse.quote(name, safe="")), asset["browser_download_url"][len(prefix):]):
            continue
        result[name] = dict(Name=name, Url=asset["browser_download_url"], Size=asset["size"], Sha256=digest[7:], RuntimeIdentifiers=known["RuntimeIdentifiers"])
        if known.get("ExecutableRelativePath"):
            result[name]["ExecutableRelativePath"] = known["ExecutableRelativePath"]
    return list(result.values())


def self_test():
    port = dict(Repository="owner/project", ReleaseTag="v1.2", Assets=[dict(Name="game-v1.2-windows-x64.zip", RuntimeIdentifiers=["win-x64"])])
    asset = dict(name="game-v1.3-windows-x64.zip", size=25, digest="sha256:" + "a"*64, browser_download_url="https://github.com/owner/project/releases/download/v1.3/game-v1.3-windows-x64.zip")
    release = dict(tag_name="v1.3", draft=False, prerelease=False, assets=[asset])
    assert len(candidates(port, release)) == 1
    assert not candidates(port, dict(release, prerelease=True))
    assert not candidates(port, dict(release, assets=[dict(asset, digest=None)]))
    assert not candidates(port, dict(release, assets=[dict(asset, browser_download_url="https://example.invalid/game.zip")]))
    assert not candidates(port, dict(release, assets=[asset, dict(asset, name="game-v1.4-windows-x64.zip")]))
    assert pattern("game-win64.zip", "v1.2") == "game-win64.zip"
    assert pattern("recharged-1.1.1+g123456789abc-windows.zip", "v1.1.1") == "recharged-*-windows.zip"
    gl = dict(port, RepositoryHost="gitlab.com")
    uploaded = dict(asset, browser_download_url="https://gitlab.com/api/v4/projects/owner%2Fproject/uploads/" + "b"*32 + "/" + asset["name"])
    assert len(candidates(gl, dict(release, assets=[uploaded]))) == 1
    assert not candidates(gl, dict(release, assets=[dict(uploaded, browser_download_url=uploaded["browser_download_url"].replace("owner%2Fproject", "other%2Fproject"))]))
    normalized = gitlab_release("owner/project", dict(tag_name="v1.3", description="[Get](/uploads/"+"b"*32+"/"+asset["name"]+")\n- `"+"a"*64+"` "+asset["name"]), lambda url:25)
    assert len(candidates(gl, normalized)) == 1
    assert not gitlab_release("owner/project", dict(tag_name="v1.3-rc1", description=""), lambda url:25)["assets"]
    print("Upstream watcher: stable versions, known package families, ambiguity, origins and digest checks passed.")


def api(route, *arguments):
    process = subprocess.run(["gh", "api", route, *arguments], capture_output=True, encoding="utf-8", timeout=45)
    if process.returncode:
        raise RuntimeError("HTTP 404" if "404" in process.stderr else "GitHub request unavailable")
    return json.loads(process.stdout)


def gitlab_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent":"DecompShelf-native-watch/1.0"}), timeout=30) as response:
        data = response.read(4*1024*1024+1)
    if len(data)>4*1024*1024:
        raise ValueError("GitLab response too large")
    return json.loads(data)


def upload_size(url):
    with urllib.request.urlopen(urllib.request.Request(url, method="HEAD", headers={"User-Agent":"DecompShelf-native-watch/1.0"}), timeout=30) as response:
        return int(response.headers.get("Content-Length", "0")) if response.url.startswith("https://") else 0


def gitlab_release(repo, release, size=upload_size):
    tag = release.get("tag_name", "")
    result = dict(tag_name=tag, assets=[])
    if release.get("upcoming_release") or re.search(r"(?:^|[-_.])(alpha|beta|rc|preview|nightly)(?:[-_.0-9]|$)", tag, re.I):
        result["prerelease"] = True
        return result
    description = release.get("description") or ""
    hashes = {}
    for inline, plain, name in re.findall(r"(?m)^(?:-[ \t]*`([a-fA-F0-9]{64})`[ \t]+|([a-fA-F0-9]{64})[ \t]{2,}\*?)([^\r\n]+)", description):
        hashes.setdefault(name.strip().replace(" ", "_"), []).append(inline or plain)
    for secret, encoded in re.findall(r"\]\(/uploads/([a-fA-F0-9]{32})/([^/()\r\n]+)\)", description):
        name = urllib.parse.unquote(encoded)
        if len(hashes.get(name, []))!=1 or re.search(r"[/\\\x00]", name) or name in (".", ".."):
            continue
        url = "https://gitlab.com/api/v4/projects/"+urllib.parse.quote(repo, safe="")+"/uploads/"+secret+"/"+urllib.parse.quote(name, safe="")
        result["assets"].append(dict(name=name, size=size(url), digest="sha256:"+hashes[name][0], browser_download_url=url))
    return result


def write(file, value):
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", "utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / ".tools/native-upstream-watch")
    parser.add_argument("--limit", type=int, help="Limit repository requests for a smoke check")
    args = parser.parse_args()
    self_test()
    if args.self_test:
        return
    output = args.output.resolve()
    if output == (ROOT / "src/CubeShelf.Desktop/Catalogs").resolve():
        parser.error("Reports must not overwrite shipped catalogues")
    entries, catalogues = [], {}
    for path in sorted((ROOT / "src/CubeShelf.Desktop/Catalogs").glob("*.json")):
        data = json.loads(path.read_text("utf-8-sig"))
        if isinstance(data, list) and data and isinstance(data[0], dict) and "Repository" in data[0]:
            catalogues[path.name] = copy.deepcopy(data)
            entries.extend((path.name, p) for p in data)
    android = json.loads((ROOT / "src/CubeShelf.Mobile/Catalogs/android-ports.json").read_text("utf-8-sig"))
    entries.extend(("android", dict(p, ReleaseTag=p["Version"])) for p in android)
    repositories = sorted({(p.get("RepositoryHost", "github.com"), p["Repository"]) for _, p in entries})
    assert all(host in ("github.com", "gitlab.com") and REPOSITORY.fullmatch(repo) for host,repo in repositories)
    if args.limit is not None:
        repositories = repositories[:max(0, args.limit)]
    output.mkdir(parents=True, exist_ok=True)
    print(f"Observing {len(repositories)} upstream repositories", flush=True)

    def inspect(address):
        host,repo=address
        result = dict(Repository=repo, RepositoryHost=host)
        if host == "gitlab.com":
            prefix = "https://gitlab.com/api/v4/projects/"+urllib.parse.quote(repo, safe="")
            try:
                head = gitlab_json(prefix+"/repository/commits?per_page=1")[0]["id"]
                if not re.fullmatch("[a-fA-F0-9]{40}",head):raise ValueError("Invalid source revision")
                result["SourceCommit"]=head
                result["Release"]=gitlab_release(repo,gitlab_json(prefix+"/releases/permalink/latest"))
            except (ValueError, KeyError, OSError, TypeError) as error:
                result["SourceError"]=str(error)
            return result
        try:
            # Keep source checks in the separate GraphQL budget; 639 repos exceed Actions' 1,000 REST requests with two GETs each.
            owner, name = repo.split("/")
            query = "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){defaultBranchRef{target{oid}}}}"
            head = api("graphql", "-f", "query=" + query, "-f", "owner=" + owner, "-f", "name=" + name)["data"]["repository"]["defaultBranchRef"]["target"]["oid"]
            if not re.fullmatch("[a-fA-F0-9]{40}", head):
                raise ValueError("Invalid source revision")
            result["SourceCommit"] = head
        except (RuntimeError, ValueError, TypeError, KeyError, OSError, subprocess.TimeoutExpired) as error:
            result["SourceError"] = str(error)
        try:
            release = api("repos/" + repo + "/releases/latest")
            if not release.get("draft") and not release.get("prerelease"):
                result["Release"] = release
        except (RuntimeError, ValueError, OSError, subprocess.TimeoutExpired) as error:
            result["ReleaseError"] = str(error)
        return result

    snapshots = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(inspect, repositories):
            snapshots[(result["RepositoryHost"],result["Repository"])] = result
            if len(snapshots) % 25 == 0:
                print(f"Observed {len(snapshots)}/{len(repositories)}", flush=True)
    changes, review = [], []
    for console, port in entries:
        result = snapshots.get((port.get("RepositoryHost", "github.com"),port["Repository"]))
        if result is None:
            continue
        release = result.get("Release", {})
        tag = release.get("tag_name", "")
        source_changed = bool(port.get("SourceCommit") and result.get("SourceCommit") and port["SourceCommit"] != result["SourceCommit"])
        release_changed = bool(tag and tag != port.get("ReleaseTag", ""))
        available = candidates(port, release) if console != "android" else []
        changed_package = any(a["Sha256"] not in {k["Sha256"] for k in port.get("Assets", [])} for a in available)
        if source_changed or release_changed or changed_package:
            item = dict(Console=console.removesuffix(".json"), Id=port["Id"], Title=port["Title"], Repository=port["Repository"], RepositoryHost=port.get("RepositoryHost","github.com"),
                PreviousRelease=port.get("ReleaseTag", ""), Release=tag, SourceChanged=source_changed,
                ReleaseChanged=release_changed, PackageChanged=changed_package, VerifiedPackageCandidates=available,
                AndroidHostRebuildRequired=console == "android", GameplayTested=False)
            changes.append(item)
            if console != "android" and available and (release_changed or changed_package):
                proposed = next(p for p in catalogues[console] if p["Id"] == port["Id"])
                host=port.get("RepositoryHost", "github.com")
                proposed.update(ReleaseTag=tag, ReleaseUrl="https://"+host+"/"+port["Repository"]+("/-/releases/" if host=="gitlab.com" else "/releases/tag/")+urllib.parse.quote(tag,safe=""), Assets=available)
            else:
                review.append(item)
    report = dict(CheckedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(), Repositories=len(snapshots), Changes=changes, Review=review,
        Errors=[dict(Repository=r["Repository"], SourceError=r.get("SourceError"), ReleaseError=r.get("ReleaseError")) for r in snapshots.values() if r.get("SourceError") or r.get("ReleaseError")])
    write(output / "report.json", report)
    for name, ports in catalogues.items():
        write(output / "desktop-catalog-candidates" / name, ports)
    summary = f"# Native upstream watch\n\n{len(snapshots)} repositories observed; {len(changes)} changed entries; {len(review)} require integration review.\n\n"
    summary += "Proposals only: shipped catalogues are unchanged. No upstream code was built or executed. No gameplay validation is claimed. Android changes require regenerated pinned hosts and a new compatible DecompShelf APK.\n"
    (output / "summary.md").write_text(summary, "utf-8")
    print(summary, flush=True)


if __name__ == "__main__":
    main()
