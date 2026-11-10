-- Targeted export: preserves every scene sprite and all four navigation arrows.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local I=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/inventory_icons.lua')
local root=P.root
local f=assert(io.open(root..'/art-manifest.json','r'))
local manifest=json.decode(f:read('*a'));f:close()
local info
for _,entry in ipairs(manifest) do if entry.id=='icons' then info=entry end end
assert(info,'Missing icons manifest')
local spr=app.open(root..'/Sources/icons.aseprite')
assert(spr and spr.width==432 and spr.height==318,'Wrong icon source canvas')
local rendered={}
for index,id in ipairs(I.order) do
 local sl,meta
 for _,layer in ipairs(spr.layers) do if layer.name==id then sl=layer end end
 for _,entry in ipairs(info.layers) do if entry.name==id then meta=entry end end
 assert(sl and meta,'Missing icon layer: '..id)
 local x,y=I.origin(index)
 local im=P.blank();im:drawImage(I.draw(id),Point(x,y))
 local cel=sl:cel(1)
 assert(cel,'Missing icon cel: '..id)
 cel.image=im;cel.position=Point(0,0)
 local x0,y0,x1,y1=P.bounds(im)
 x0=x0-2;y0=y0-2;x1=x1+2;y1=y1+2
 -- Keep the authored robot frame stable when its silhouette changes.
 if id=='robot' then x0=x+3;y0=y-1;x1=x0+33;y1=y0+35 end
 local crop=Image(im,Rectangle(x0,y0,x1-x0+1,y1-y0+1))
 assert(crop.width<=40 and crop.height<=36,'Icon exceeds inventory area: '..id..' '..crop.width..'x'..crop.height)
 for px in crop:pixels() do
  assert(px()==P.R or px()==P.B or app.pixelColor.rgbaA(px())==0,'Extra ink: '..id)
 end
 meta.x=x0;meta.y=y0;meta.w=crop.width;meta.h=crop.height
 rendered[#rendered+1]={image=crop,path=root..'/Sprites/'..meta.file,id=id}
end
-- Validate all eight before replacing any export.
for _,item in ipairs(rendered) do
 item.image:saveAs(item.path)
 print(item.id..': '..item.image.width..'x'..item.image.height)
end
spr:saveAs(root..'/Sources/icons.aseprite')
local composite=P.blank(432,318,P.B);composite:drawSprite(spr,1)
composite:saveAs(root..'/icons.png')
local red=0;for px in composite:pixels() do if px()==P.R then red=red+1 end end
info.redCoverage=red/(432*318)
f=assert(io.open(root..'/art-manifest.json','w'));f:write(json.encode(manifest));f:close()
spr:close()
print('INVENTORY_ICONS_UPDATED')
