# Rebuilds the Dock icon atlas that build.cmd embeds.
#   src\DockIcons.png   5 x 1 cells of 128 px (Dock system buttons)
# Cell order is make_icons.py IDS, which DockArt.cs mirrors.
# Needs Python 3 and Microsoft Edge (headless, transparent background).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)
python (Join-Path $here 'make_icons.py')
if ($LASTEXITCODE) { throw 'make_icons.py failed' }

$ids = 'start','taskview','work','suruh','shelf'
$cell = 128
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('deskmate-dock-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$out = Join-Path $root 'src\DockIcons.png'
try {
    $w = $cell * $ids.Count; $h = $cell
    $cells = for ($i = 0; $i -lt $ids.Count; $i++) {
        $src = ([Uri](Join-Path $here "svg\dock-$($ids[$i]).svg")).AbsoluteUri
        "<img src='$src' style='position:absolute;left:$($i * $cell)px;top:0;width:${cell}px;height:${cell}px'>"
    }
    $page = Join-Path $tmp 'dock.html'
    Set-Content -LiteralPath $page -Encoding UTF8 -Value ("<!doctype html><style>html,body{margin:0;background:transparent;overflow:hidden}</style>" + ($cells -join ''))
    $shot = Join-Path $tmp 'dock.png'
    $edgeArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--user-data-dir=`"$tmp\profile`"",
        "--window-size=$w,$h", "--screenshot=`"$shot`"", ([Uri]$page).AbsoluteUri)
    Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -WindowStyle Hidden -RedirectStandardError (Join-Path $tmp 'dock.log')
    for ($t = 0; $t -lt 50 -and -not (Test-Path $shot); $t++) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $shot)) { throw 'Edge did not render the dock atlas' }
    Copy-Item -LiteralPath $shot -Destination $out -Force
    python -c "import sys; from PIL import Image; Image.open(sys.argv[1]).save(sys.argv[1], optimize=True)" $out
    Write-Host "OK $out (${w}x${h})"
}
finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
