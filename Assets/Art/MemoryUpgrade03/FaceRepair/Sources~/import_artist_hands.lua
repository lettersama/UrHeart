-- Import the user's edited pixels verbatim. Never redraw or resave their file.
local root=assert(_G.REPAIR_BUST_SOURCE_ROOT,
 'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
local sourcePath=root..'ManualEdit/hands_edit.aseprite'
local source=app.open(sourcePath);local pc=app.pixelColor
assert(source.width==116 and source.height==116 and #source.frames==4,'Unexpected edited hand layout')
assert(#source.layers==3,'Edited hand layer count changed')
local P=dofile(root..'animation_draw.lua').new(216,162)
local kinds={'cloth','pencil','puff','paste'};local keys={'hand','tool','fingers'}
local imported={};local report={status='PASS',source='ManualEdit/hands_edit.aseprite',sourceSha256='73df3263189f7d57ab1551936585743372392cd889714bb0ee8f3d2286bbef6e',versions={}}
for fi,kind in ipairs(kinds) do
 local original=Image(116,116,ColorMode.RGB);original:clear(0);original:drawSprite(source,fi,Point(0,0))
 local entry={layers={},original=original}
 for li,key in ipairs(keys) do
  local im=P.image();local layer=source.layers[li];local cel=layer:cel(fi)
  assert(layer.isVisible and layer.opacity==255,'Hand layer visibility or opacity changed')
  if cel then im:drawImage(cel.image,cel.position) end
  entry.layers[key]=im
 end
 local combined=P.compose({entry.layers.hand,entry.layers.tool,entry.layers.fingers})
 for y=0,161 do for x=0,215 do
  local expected=(x<116 and y<116) and original:getPixel(x,y) or 0
  assert(combined:getPixel(x,y)==expected,'Edited pixel or layer composition changed on '..kind)
 end end
 imported[kind]=entry
end
source:close()
local function palette(sprite)
 local p=Palette(3);p:setColor(0,Color{r=0,g=0,b=0,a=0});p:setColor(1,Color{r=0,g=0,b=0,a=255});p:setColor(2,Color{r=135,g=130,b=126,a=255});sprite:setPalette(p)
end
for _,variant in ipairs({{'Native216',1}}) do
 local folder,mult=variant[1],variant[2];local out=root..folder..'/Hands/';app.fs.makeAllDirectories(out..'Layers')
 local sprite=Sprite(216*mult,162*mult,ColorMode.RGB);palette(sprite);local layers={}
 for i,name in ipairs({'01 Artist hand and short cuff','02 Artist tool','03 Artist foreground fingers and ink'}) do local l=i==1 and sprite.layers[1] or sprite:newLayer();l.name=name;layers[i]=l end
 local poses={}
 for fi,kind in ipairs(kinds) do
  if fi>1 then sprite:newEmptyFrame() end;sprite.frames[fi].duration=1
  local full=Image(216*mult,162*mult,ColorMode.RGB);full:clear(0)
  for li,key in ipairs(keys) do
   local im=Image(216*mult,162*mult,ColorMode.RGB);im:clear(0)
   for y=0,161 do for x=0,215 do local pixel=imported[kind].layers[key]:getPixel(x,y)
    if pc.rgbaA(pixel)>0 then
     assert(pixel==P.B or pixel==P.L,'Unexpected artist palette')
     for dy=0,mult-1 do for dx=0,mult-1 do im:drawPixel(x*mult+dx,y*mult+dy,pixel) end end
    end
   end end
   sprite:newCel(layers[li],fi,im,Point(0,0));im:saveAs(out..'Layers/'..kind..'_'..key..'.png');full:drawImage(im,Point(0,0))
  end
  for y=0,162*mult-1 do for x=0,216*mult-1 do
   local sx,sy=math.floor(x/mult),math.floor(y/mult)
   local expected=(sx<116 and sy<116) and imported[kind].original:getPixel(sx,sy) or 0
   assert(full:getPixel(x,y)==expected,'Imported hand pixels differ')
  end end
  full:saveAs(out..kind..'.png')
  poses[#poses+1]={id=kind,frame=fi-1,png=kind..'.png',anchor={x=25*mult,y=25*mult}}
 end
 -- Create tags after all frames exist so later insertions cannot extend them.
 for fi,kind in ipairs(kinds) do sprite:newTag(fi,fi).name=kind end
 sprite:saveAs(out..'floating_hands.aseprite');sprite:close()
 local reopened=app.open(out..'floating_hands.aseprite')
 for fi,kind in ipairs(kinds) do
  local actual=Image(216*mult,162*mult,ColorMode.RGB);actual:clear(0);actual:drawSprite(reopened,fi,Point(0,0))
  local expected=Image{fromFile=out..kind..'.png'}
  for y=0,actual.height-1 do for x=0,actual.width-1 do assert(actual:getPixel(x,y)==expected:getPixel(x,y),'Reopened hand mismatch') end end
 end
 for _,tag in ipairs(reopened.tags) do assert(tag.fromFrame.frameNumber==tag.toFrame.frameNumber,'Hand tag is not a single pose') end
 reopened:close()
 local info={canvas={width=216*mult,height=162*mult},palette={'#000000','#87827e'},poses=poses,source='Artist-edited hands_edit.aseprite',sourceSha256=report.sourceSha256,pixelDifferencesFromArtist=0,method='Exact native pixel import; integer nearest-neighbor duplication for the 432 variant; rigid translation only.'}
 local f=assert(io.open(out..'manifest.json','w'));f:write(json.encode(info));f:close()
 report.versions[#report.versions+1]={folder=folder,poses=4,layers=3,pixelDifferencesFromArtist=0,reopened=true}
end
local f=assert(io.open(root..'verification_hands.json','w'));f:write(json.encode(report));f:close()
print('Artist hand import PASS: four poses, exact native pixels, three layers, native Aseprite source reopened.')
