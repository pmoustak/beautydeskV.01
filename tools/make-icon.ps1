# Draws the beautydesk icon (a blue pixel-art lightning bolt) into beautydesk.ico.
# Each size is the 16x16 design scaled by a whole number, so the pixels stay sharp.
Add-Type -AssemblyName System.Drawing

$grid = @(
    '................',
    '.........oooooo.',
    '........ohhbbbo.',
    '.......ohhbbdo..',
    '......ohhbbdo...',
    '.....ohhbbdo....',
    '....ohhbbbooooo.',
    '...ohhhhbbbbbbo.',
    '...oooooohbbdo..',
    '........ohbdo...',
    '.......ohbdo....',
    '......ohbdo.....',
    '.....ohbdo......',
    '....ohdo........',
    '....ooo.........',
    '................'
)
$colours = @{
    'o' = [Drawing.Color]::FromArgb(255, 10, 26, 64)     # navy outline
    'h' = [Drawing.Color]::FromArgb(255, 165, 216, 255)  # highlight
    'b' = [Drawing.Color]::FromArgb(255, 61, 139, 255)   # blue
    'd' = [Drawing.Color]::FromArgb(255, 31, 95, 214)    # shade
}

$sizes = 16, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $scale = $size / 16
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::Transparent)
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            $c = [string]$grid[$y][$x]
            if ($colours.ContainsKey($c)) {
                $brush = New-Object Drawing.SolidBrush $colours[$c]
                $g.FillRectangle($brush, $x * $scale, $y * $scale, $scale, $scale)
                $brush.Dispose()
            }
        }
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# .ico container with PNG-compressed images
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
$target = Join-Path (Split-Path -Parent $PSScriptRoot) 'beautydesk.ico'
[IO.File]::WriteAllBytes($target, $out.ToArray())
"wrote $target ($($out.Length) bytes)"

# preview of the 256 px image next to the icon
[IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $PSScriptRoot) 'icon-preview.png'), $images[-1])
