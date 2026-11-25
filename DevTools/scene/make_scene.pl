#!/usr/bin/perl
# Writes "Assets/Metroid Prime Scene.unity": the uploader (scripting on, assembly SomniumMetroid, spawn on the plain floor at 0,0.1,0),
# a directional light, the "Metroid Game" root (MpGame + its shader carriers), the spawn floor (invisible collider), the global
# volume and Somnium Rules (no fly / glide). No networking objects (iteration 1 is single player).
# Settings blocks (occlusion, render, lightmap, navmesh) are copied from the WoW Dreamport's client-tested scene (fog ON:
# a scene with fog off strips every fog variant from the upload - the ETS finding). Adapted from the Skyrim Dreamport's make_scene.pl.
use strict;
use warnings;

my $wowScene = 'D:/Dreamport/WoW/World Of Warcraft/Assets/World of Warcraft Scene.unity';
my $out = 'D:/Dreamport/Metroid Prime/METROIDPRIME/Assets/Metroid Prime Scene.unity';
my %g = (
    game => '6d1fa955114cb25f18d5d28a525ac5e0',
    scenenet => '34ff1f6671d889076560a6c54755779e',
    asmdef => '52987c20bccc69318da5c8e5abd02897',
    volume => 'db45a28408aa7eb4a88ca76ecf6bf5fd',
    data64 => '0b8147e129a49fd49ba7bdc9dc75daaa',
    events => '6ba26196d4959fb45a90fa1113c622e9',
    netobj => 'e725a070cec140c4caffb81624c8c787',
    bridge => '6af85501506cb7f4db5388060eadf518',
);
my @carriers = ();
my $tevMat = '104c6eccd796107b02de9b708d33f6e5';
my $fxMat = 'd029761792a56cb1d887b57d3ab36fe3';

open(my $in, '<', $wowScene) or die "$wowScene: $!";
local $/; my $wow = <$in>; close $in;
$wow =~ s/\r\n/\n/g;

# the settings blocks: everything before the first GameObject
my ($head) = $wow =~ /\A(.*?)(?=--- !u!1 &)/s or die "no head";
# the directional light (GameObject 200000 .. its UniversalAdditionalLightData 200003)
my ($light) = $wow =~ /(--- !u!1 &200000\n.*?)(?=--- !u!1 &300000)/s or die "no light";
# Somnium Rules (glide + fly rules)
my ($rules) = $wow =~ /(--- !u!1 &1205748433\n.*?)(?=--- !u!1 &3300000001)/s or die "no rules";

my $s = $head;
$s .= <<"EOF";
--- !u!1 &100000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 100001}
  - component: {fileID: 100003}
  - component: {fileID: 100002}
  m_Layer: 0
  m_Name: Uploader
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &100001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &100002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: -553830836, guid: 5a46fca9cfe00e84b83457af8d644413, type: 3}
  m_Name:
  m_EditorClassIdentifier: SomniumSpace.SDK.dll::SomniumSpace.SDK.Configs.WorldSDKConfig
  Config: {fileID: 100003}
  DoCleanBuild: 1
  AssetName: Metroid Prime
  AssetDescription: 'Metroid Prime Dreamport - play Metroid Prime in Somnium Space, in VR or on a monitor, from your own copy of the game.'
  PreviewPicturePath:
  PreviewTexture: {fileID: 0}
  LastBuildSizeBytes: 0
  BoundsIsDisplay: 0
  IsOpen360Preview: 0
  IsEditSpawnPos: 0
  ShowRespawnHeight: 0
  ValidationData: []
  InfoIsOpen: 0
  Preview360PicturePath:
  Preview360Texture: {fileID: 0}
  Preview360Location: {x: 0, y: 0, z: 0}
  blockchainType: 3
  WorldSizeType: 0
  WorldSizeMB: 0
  WorldIdForUpload: 0
  DoReplaceExistingWorld: 0
  OpenScriptingSettings: 1
  EnableScripting: 1
  AssemblyIndex: 0
  ScriptingAssembly: SomniumMetroid
  AssemblyAvailabilityCorrect: 1
  AssemblyVisualise: 0
  AsmdefReference: {fileID: 5897886265953266890, guid: $g{asmdef}, type: 3}
  SelectedIndexForSize: 0
  OwnedTokens: []
  OwnedWorldSizes: 00000000
  SelectedIndexForWorldToReplace: 0
  UploadedWorlds: []
  CheckedScriptingAssembly: SomniumMetroid
  TipJarUsed: 0
  TIpJarServerError:
--- !u!114 &100003
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 252206447, guid: ca5d94d337ec5644a94dfc0f52bb6771, type: 3}
  m_Name:
  m_EditorClassIdentifier: SomniumSpace.Bundles.dll::SomniumSpace.Bundles.Configs.WorldConfig
  IsCanMint: 1
  Framework:
    SDKVersion:
      Major: 3
      Minor: 1
      Patch: 8
    UnityVersion: 6000.3.5f2
    PakagesInfo:
    - Type: 0
      Version: 17.3.0
    - Type: 1
      Version: 1.16.1
  Attributes:
    Container: []
  Tags: 0
  SpawnPointPos: {x: 0, y: 0.1, z: 0}
  SpawnPointDirection: 0
  RespawnHeightY: -100
  RespawningBehavior: 0
  Subcategory: 1
EOF
$s .= $light;
$s .= <<"EOF";
--- !u!1 &300000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 300001}
  - component: {fileID: 300002}
  m_Layer: 0
  m_Name: Metroid Game
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &300001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 300000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &300002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 300000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: $g{game}, type: 3}
  m_Name:
  m_EditorClassIdentifier: SomniumMetroid::SomniumSpace.Worlds.SomniumMetroid.Game.MpGame
  _tevMaterial: {fileID: 2100000, guid: $tevMat, type: 2}
  _fxMaterial: {fileID: 2100000, guid: $fxMat, type: 2}
--- !u!1 &400000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 400001}
  - component: {fileID: 400002}
  m_Layer: 0
  m_Name: Spawn Floor (fallback collider)
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &400001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 400000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: -0.5, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!65 &400002
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 400000}
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: {x: 24, y: 1, z: 24}
  m_Center: {x: 0, y: 0, z: 0}
--- !u!1 &500000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 500002}
  - component: {fileID: 500001}
  m_Layer: 0
  m_Name: Global Volume
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!114 &500001
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 500000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 172515602e62fb746b5d573b38a5fe58, type: 3}
  m_Name:
  m_EditorClassIdentifier: Unity.RenderPipelines.Core.Runtime::UnityEngine.Rendering.Volume
  m_IsGlobal: 1
  priority: 0
  blendDistance: 0
  weight: 1
  sharedProfile: {fileID: 11400000, guid: $g{volume}, type: 2}
--- !u!4 &500002
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 500000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
EOF
$s .= $rules;


$s .= "--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
" . join("", map { "  - {fileID: $_}
" } (100001, 200001, 300001, 400001, 500002, 1205748436));

open(my $o, ">", $out) or die "$out: $!";
binmode $o;
print $o $s;
close $o;
print "wrote $out (" . length($s) . " bytes)\n";
