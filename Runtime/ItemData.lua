#! /usr/bin/env vrc_class_gen.lua

return {
  is_class_definition = true,
  usings = {
    "UdonSharp",
    "UnityEngine",
    "VRC.SDKBase",
    "VRC.Udon",
  },
  namespace = "JanSharp",
  class_name = "ItemData",
  fields = {
    {type = "uint", name = "id"},
    {type = "int", name = "prefabIndex"},
    {type = "Vector3", name = "position"},
    {type = "Quaternion", name = "rotation"},
    -- {type = "float", name = "scale", default = "1f"},
    {type = "bool", name = "isAttached"},
    {type = "int", name = "holdingPlayerId", default = "-1"},
    {type = "VRCPlayerApi.TrackingDataType", name = "attachedTracking"},
    {type = "ItemSync", name = "inst"}, -- Not part of game state.
  },
}
