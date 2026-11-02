# cap2.ps1 — cattura viewport corrente (PIE) senza trasformata
. "$PSScriptRoot\ue_common.ps1"
$cap = @{ annotations = @{gridSpacing = 0.0; maxLabelDistance = 0.0 } }
$r = Ue "EditorToolset.EditorAppToolset" "CaptureViewport" $cap
$img = $r.image.data
[IO.File]::WriteAllBytes("$PSScriptRoot\cap_pie.png", [Convert]::FromBase64String($img))
Write-Output "SAVED $($img.Length)"
