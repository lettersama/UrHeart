$ErrorActionPreference = 'Stop'
$authorRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectDirectory = Get-Item -LiteralPath $authorRoot
while ($null -ne $projectDirectory -and -not (Test-Path -LiteralPath (Join-Path $projectDirectory.FullName 'ProjectSettings/ProjectVersion.txt'))) {
    $projectDirectory = $projectDirectory.Parent
}
if ($null -eq $projectDirectory) { throw 'Cannot find the Unity project above the FaceRepair sources.' }
$projectRoot = $projectDirectory.FullName
$nativeRoot = [IO.Path]::GetFullPath((Join-Path $authorRoot 'Native216'))
$assetRoot = [IO.Path]::GetFullPath((Join-Path $authorRoot '../Runtime'))
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'))) { throw 'Wrong Unity project root.' }

function Assert-Within([string]$Path, [string]$Root) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($Root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped its allowed root: $resolved"
    }
    return $resolved
}

function Ensure-SpriteMeta([string]$Path) {
    $metaPath = $Path + '.meta'
    if (Test-Path -LiteralPath $metaPath) { return }
    $guid = [guid]::NewGuid().ToString('N')
    $spriteGuid = [guid]::NewGuid().ToString('N')
    $meta = @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 512
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 20
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 512
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: $spriteGuid
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    [IO.File]::WriteAllText($metaPath, ($meta.TrimEnd() + "`n"), [Text.UTF8Encoding]::new($false))
}

$manifestPath = Join-Path $nativeRoot 'Animation/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$relativeFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('repair_bust.png','robot.png','robot_mask.png','empty_workbench.png','cloth.png','repair_paste.png','face_without_scratches.png')) { [void]$relativeFiles.Add($name) }
foreach ($name in @('cloth','pencil','puff','paste')) { [void]$relativeFiles.Add("Hands/$name.png") }
foreach ($state in $manifest.states) {
    [void]$relativeFiles.Add('Animation/' + $state.patch)
    foreach ($frame in $state.frames) { [void]$relativeFiles.Add('Animation/' + $frame.delta) }
}
foreach ($action in $manifest.actions) {
    [void]$relativeFiles.Add('Animation/' + $action.resultPatch)
    foreach ($frame in $action.frames) { [void]$relativeFiles.Add('Animation/' + $frame.delta) }
}
foreach ($part in @('clean','filled','powder_left','powder_right','brows','cat','heart')) { [void]$relativeFiles.Add("Animation/PatchParts/$part.png") }

# Validate the complete plan before moving any files. Existing metas retain their GUIDs.
$plan = foreach ($relative in ($relativeFiles | Sort-Object)) {
    $source = Assert-Within (Join-Path $nativeRoot $relative) $nativeRoot
    $destination = Assert-Within (Join-Path $assetRoot $relative) $assetRoot
    if (Test-Path -LiteralPath $source -PathType Leaf) { $contentPath = $source }
    elseif (Test-Path -LiteralPath $destination -PathType Leaf) { $contentPath = $destination }
    else { throw "Missing production asset: $relative" }
    [pscustomobject]@{ relative=$relative; source=$source; destination=$destination; sha256=(Get-FileHash -LiteralPath $contentPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$moves = 0
foreach ($item in $plan) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($item.destination))
    Ensure-SpriteMeta $item.destination
    if (Test-Path -LiteralPath $item.source -PathType Leaf) {
        Move-Item -LiteralPath $item.source -Destination $item.destination -Force
        $moves++
    }
    if ((Get-FileHash -LiteralPath $item.destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "Move changed bytes: $($item.relative)" }
}

# The game manifest describes only files installed beside it. Authoring Aseprite,
# GIFs and full-frame comparisons stay in the ignored Sources~ directory.
$runtimeManifest = $manifest
[void]$runtimeManifest.Remove('master')
[void]$runtimeManifest.handRendering.Remove('layeredMaster')
$runtimeManifest.handRendering.staticPoses = '../Hands/manifest.json'
$runtimeManifest.assetLayout = 'Runtime patch PNGs; paths are relative to this Animation directory.'
foreach ($state in $runtimeManifest.states) {
    foreach ($key in @('aseprite','gif','open')) { [void]$state.Remove($key) }
    foreach ($frame in $state.frames) { [void]$frame.Remove('png') }
}
foreach ($action in $runtimeManifest.actions) {
    foreach ($key in @('aseprite','gif')) { [void]$action.Remove($key) }
    foreach ($frame in $action.frames) { [void]$frame.Remove('png') }
}
ConvertTo-Json -InputObject $runtimeManifest -Depth 32 | Set-Content -LiteralPath (Join-Path $assetRoot 'Animation/manifest.json') -Encoding utf8NoBOM
$handManifestSource = Join-Path $nativeRoot 'Hands/manifest.json'
$handManifestDestination = Join-Path $assetRoot 'Hands/manifest.json'
if (Test-Path -LiteralPath $handManifestSource) {
    [void](Assert-Within $handManifestSource $nativeRoot)
    [void](Assert-Within $handManifestDestination $assetRoot)
    Move-Item -LiteralPath $handManifestSource -Destination $handManifestDestination -Force
}
@{status='PASS';runtimeRoot=$assetRoot;pngCount=$plan.Count;movedPngs=$moves;files=@($plan | Select-Object relative,sha256)} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $authorRoot 'runtime_inventory.json') -Encoding utf8NoBOM
Write-Output "Published $($plan.Count) PNG assets ($moves moved) with original bytes and Sprite import settings."
