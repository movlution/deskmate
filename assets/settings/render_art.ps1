# Rebuilds the Settings artwork atlases that build.cmd embeds.
#   src\SettingsIcons.png   7 x 2 cells of 128 px (category icon tiles)
#   src\SettingsScenes.png  2 x 7 cells of 400 x 160 px (hero banner scenes)
# Cell order is make_art.py IDS, which SettingsArt.cs mirrors.
# Needs Python 3 and Microsoft Edge (headless, transparent background).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)
python (Join-Path $here 'make_art.py')
if ($LASTEXITCODE) { throw 'make_art.py failed' }

$ids = 'general','look','island','dock','messages','work','remind','ai','agents','ingat','files','system','game','keys'
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('deskmate-art-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Render-Atlas([string]$kind, [int]$cw, [int]$ch, [int]$cols, [string]$out) {
    $rows = [Math]::Ceiling($ids.Count / $cols)
    $w = $cw * $cols; $h = $ch * $rows
    $cells = for ($i = 0; $i -lt $ids.Count; $i++) {
        $x = ($i % $cols) * $cw; $y = [Math]::Floor($i / $cols) * $ch
        $src = ([Uri](Join-Path $here "svg\$kind-$($ids[$i]).svg")).AbsoluteUri
        "<img src='$src' style='position:absolute;left:${x}px;top:${y}px;width:${cw}px;height:${ch}px'>"
    }
    $page = Join-Path $tmp "$kind.html"
    Set-Content -LiteralPath $page -Encoding UTF8 -Value ("<!doctype html><style>html,body{margin:0;background:transparent;overflow:hidden}</style>" + ($cells -join ''))
    $shot = Join-Path $tmp "$kind.png"
    # Edge logs harmless noise to stderr; keep it away from ErrorActionPreference=Stop
    $edgeArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--user-data-dir=`"$tmp\profile`"",
        "--window-size=$w,$h", "--screenshot=`"$shot`"", ([Uri]$page).AbsoluteUri)
    Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -WindowStyle Hidden -RedirectStandardError (Join-Path $tmp "$kind.log")
    for ($t = 0; $t -lt 50 -and -not (Test-Path $shot); $t++) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $shot)) { throw "Edge did not render $kind" }
    Copy-Item -LiteralPath $shot -Destination $out -Force
    # Edge writes loosely compressed PNGs; recompress losslessly (Pillow, optional)
    python -c "import sys; from PIL import Image; Image.open(sys.argv[1]).save(sys.argv[1], optimize=True)" $out
    Write-Host "OK $out (${w}x${h})"
}

try {
    Render-Atlas 'icon' 128 128 7 (Join-Path $root 'src\SettingsIcons.png')
    Render-Atlas 'scene' 400 160 2 (Join-Path $root 'src\SettingsScenes.png')
}
finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
