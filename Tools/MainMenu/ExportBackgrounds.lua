-- Aseprite --batch --script-param root=<project> [--script-param output=<dir>]
--          --script Tools/MainMenu/ExportBackgrounds.lua
-- Export only the room. RainScene is rendered separately in Unity behind it.
local root = assert(app.params.root, "Pass --script-param root=<project path>")
local output = app.params.output or (root .. "/Assets/Art/UI/NewMainMenu")
local sprite = assert(app.open(root .. "/Assets/Art/UI/NewMainMenu/NewMainMenu.aseprite"))

local function child(layers, name)
  for _, layer in ipairs(layers) do
    if layer.name == name then return layer end
  end
  error("Missing background layer: " .. name)
end

local room = child(sprite.layers, "2")
local variants = {
  {name="NoCarnation", dark="NewMainMenu_NoCarnation.png", light="NewMainMenu_Light_NoCarnation.png"},
  {name="Carnation", dark="NewMainMenu_Carnation.png", light="NewMainMenu_Light_Carnation.png"}
}
for _, variant in ipairs(variants) do
  variant.layer = child(room.layers, variant.name)
  variant.darkLayer = child(variant.layer.layers, "Dark")
  variant.lightLayer = child(variant.layer.layers, "Light")
end

for _, layer in ipairs(sprite.layers) do layer.isVisible = layer == room end
for _, variant in ipairs(variants) do
  for _, layer in ipairs(room.layers) do layer.isVisible = layer == variant.layer end
  for _, state in ipairs({"dark", "light"}) do
    variant.darkLayer.isVisible = state == "dark"
    variant.lightLayer.isVisible = state == "light"
    sprite:saveCopyAs(output .. "/" .. variant[state])
  end
end
sprite:close()
