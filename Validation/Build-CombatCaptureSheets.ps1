Add-Type -AssemblyName System.Drawing
$captureFolder = Join-Path $PSScriptRoot '../Screenshots/EnemyUpgrade'
$indices = @(0, 2, 4, 6, 8, 10, 12, 13)
foreach ($prefix in @('enemy-fullbody', 'guard-recoil')) {
    $sheet = New-Object System.Drawing.Bitmap 1920, 700
    $graphics = [System.Drawing.Graphics]::FromImage($sheet)
    $graphics.Clear([System.Drawing.Color]::FromArgb(13, 21, 29))
    $font = New-Object System.Drawing.Font 'Segoe UI', 13
    try {
        for ($i = 0; $i -lt $indices.Count; $i++) {
            $frameName = '{0}-{1:00}.png' -f $prefix, $indices[$i]
            $framePath = Join-Path $captureFolder $frameName
            $frameImage = [System.Drawing.Image]::FromFile($framePath)
            try {
                $left = ($i % 4) * 480
                $top = [math]::Floor($i / 4) * 350
                $graphics.DrawImage($frameImage, [System.Drawing.Rectangle]::new($left, $top, 480, 320))
                $graphics.DrawString($frameName, $font, [System.Drawing.Brushes]::White, [single]($left + 10), [single]($top + 324))
            } finally { $frameImage.Dispose() }
        }
        $sheet.Save((Join-Path $captureFolder ($prefix + '-sequence.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $font.Dispose(); $graphics.Dispose(); $sheet.Dispose() }
}
