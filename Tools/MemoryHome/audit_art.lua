-- Read-back audit and contact sheet through Aseprite MCP.
local root="H:/Unity6/MrTGame/Assets/Art/SketchSrouce/MemoryHome"
local f=assert(io.open(root.."/art-manifest.json","r"))
local manifest=json.decode(f:read("*a"));f:close()
local R=app.pixelColor.rgba(165,39,55,255)
local B=app.pixelColor.rgba(0,0,0,255)
local G=app.pixelColor.rgba(135,130,123,255)
local sceneCount,spriteCount=0,0
local report={palette={"#000000","#A52737"},navigationColor="#87827B",scenes={},states={},spriteCount=0}
local sheet=Image(432*4,318*4,ColorMode.RGB);sheet:clear(B)
local stateSheet=Image(432*4,318*3,ColorMode.RGB);stateSheet:clear(B)
local function check(image,alphaAllowed,file,navigationGray)
  for it in image:pixels() do
    local c=it()
    assert(c==R or c==B or (navigationGray and c==G) or (alphaAllowed and app.pixelColor.rgbaA(c)==0),"Palette/alpha drift: "..file)
  end
end
for _,scene in ipairs(manifest) do
  local preview=Image{fromFile=root.."/"..scene.id..".png"}
  assert(preview.width==432 and preview.height==318,"Canvas mismatch: "..scene.id)
  check(preview,false,scene.id,scene.id=="icons")
  local source=assert(app.open(root.."/Sources/"..scene.id..".aseprite"))
  assert(source.width==432 and source.height==318,"Source mismatch: "..scene.id)
  for _,cel in ipairs(source.cels) do check(cel.image,true,scene.id.."/"..cel.layer.name,scene.id=="icons" and cel.layer.name:match("^arrow_")~=nil) end
  source:close()
  local statePath=root..'/States/'..scene.id..'_after.png'
  if app.fs.isFile(statePath) then
    local after=Image{fromFile=statePath}
    assert(after.width==432 and after.height==318,'State canvas mismatch: '..scene.id)
    check(after,false,statePath)
    local i=#report.states
    stateSheet:drawImage(after,Point((i%4)*432,math.floor(i/4)*318))
    report.states[#report.states+1]={id=scene.id,contactSheetCell=i+1}
  end
  for _,layer in ipairs(scene.layers) do
    local file=root.."/Sprites/"..layer.file
    local img=Image{fromFile=file}
    assert(img.width==layer.w and img.height==layer.h,"Crop mismatch: "..file)
    check(img,true,file,scene.id=="icons" and layer.name:match("^arrow_")~=nil);spriteCount=spriteCount+1
  end
  if scene.id~="icons" then
    local position=Point((sceneCount%4)*432,math.floor(sceneCount/4)*318)
    sheet:drawImage(preview,position)
    sceneCount=sceneCount+1
    report.scenes[#report.scenes+1]={id=scene.id,width=432,height=318,redCoverage=scene.redCoverage,contactSheetCell=sceneCount}
  end
end
report.spriteCount=spriteCount
app.fs.makeAllDirectories("H:/Unity6/MrTGame/Docs/MemoryHome")
sheet:saveAs("H:/Unity6/MrTGame/Docs/MemoryHome/scene-contact-sheet.png")
stateSheet:saveAs('H:/Unity6/MrTGame/Docs/MemoryHome/state-contact-sheet.png')
local out=assert(io.open("H:/Unity6/MrTGame/Tools/MemoryHome/art-audit.json","w"))
out:write(json.encode(report));out:close()
print("PASS: "..sceneCount.." native 432x318 scenes; "..spriteCount.." sprites; black/red artwork, #87827B navigation arrows, binary alpha.")
