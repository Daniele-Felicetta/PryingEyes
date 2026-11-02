# cap.ps1 — cattura viewport e salva PNG
. "$PSScriptRoot\ue_common.ps1"
$cap = @{
  captureTransform = @{
    location = @{x = 0.0; y = 1500.0; z = 400.0 }
    rotation = @{pitch = -15.0; yaw = -90.0; roll = 0.0 }
  }
  annotations = @{gridSpacing = 0.0; maxLabelDistance = 0.0 }
}
$r = Ue "EditorToolset.EditorAppToolset" "CaptureViewport" $cap
$img = $r.image.data
[IO.File]::WriteAllBytes("$PSScriptRoot\cap_view.png", [Convert]::FromBase64String($img))
Write-Output "SAVED $($img.Length) chars"
