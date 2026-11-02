# cap3.ps1 — vista ravvicinata attori di test
. "$PSScriptRoot\ue_common.ps1"
$cap = @{
  captureTransform = @{
    location = @{x = 0.0; y = 1950.0; z = 260.0 }
    rotation = @{pitch = -12.0; yaw = -90.0; roll = 0.0 }
  }
  annotations = @{gridSpacing = 0.0; maxLabelDistance = 0.0 }
}
$r = Ue "EditorToolset.EditorAppToolset" "CaptureViewport" $cap
$img = $r.image.data
[IO.File]::WriteAllBytes("$PSScriptRoot\cap_view.png", [Convert]::FromBase64String($img))
Write-Output "SAVED $($img.Length)"
