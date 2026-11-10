-- Update only the window/living-room source documents and affected sprite exports.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local W=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/window_progress_art.lua')
local root=P.root
local f=assert(io.open(root..'/art-manifest.json','r'));local manifest=json.decode(f:read('*a'));f:close()
local function sceneInfo(id) for _,v in ipairs(manifest) do if v.id==id then return v end end;error(id) end
local function layerByName(spr,name) for _,l in ipairs(spr.layers) do if l.name==name then return l end end end
local function imageOf(layer)
 local full=P.blank();local cel=assert(layer:cel(1));full:drawImage(cel.image,cel.position);return full
end
local function put(spr,info,name,img)
 local layer=layerByName(spr,name) or spr:newLayer();layer.name=name;layer.isVisible=false
 local cel=layer:cel(1)
 if cel then cel.image=img;cel.position=Point(0,0) else spr:newCel(layer,1,img,Point(0,0)) end
 local x0,y0,x1,y1=P.bounds(img)
 x0=math.max(0,x0-2);y0=math.max(0,y0-2);x1=math.min(431,x1+2);y1=math.min(317,y1+2)
 local crop=Image(img,Rectangle(x0,y0,x1-x0+1,y1-y0+1));local file=info.id..'_'..name..'.png'
 crop:saveAs(root..'/Sprites/'..file)
 local entry={name=name,file=file,x=x0,y=y0,w=crop.width,h=crop.height,visible=false};local found=false
 for i,v in ipairs(info.layers) do if v.name==name then info.layers[i]=entry;found=true;break end end
 if not found then info.layers[#info.layers+1]=entry end
end
local function render(spr,path)
 local img=P.blank(432,318,P.B);img:drawSprite(spr,1);img:saveAs(path);return img
end
local window=app.open(root..'/Sources/window.aseprite');local winInfo=sceneInfo('window');local boards={}
for i=0,2 do boards[i+1]=imageOf(assert(layerByName(window,'board_'..i))) end
for i=0,5 do
 put(window,winInfo,'nail_marker_'..i,W.marker(i))
 put(window,winInfo,'nail_'..i,W.raisedNail(i))
end
window:saveAs(root..'/Sources/window.aseprite')
for i=0,2 do layerByName(window,'board_'..i).isVisible=true end
for i=0,5 do layerByName(window,'nail_marker_'..i).isVisible=true end
render(window,root..'/States/window_markers.png')
layerByName(window,'nail_marker_0').isVisible=false;layerByName(window,'nail_0').isVisible=true
render(window,root..'/States/window_nail_placed.png')
layerByName(window,'nail_0').isVisible=false;layerByName(window,'nail_done_0').isVisible=true
render(window,root..'/States/window_one_nail_done.png')
for i=0,5 do layerByName(window,'nail_marker_'..i).isVisible=false;layerByName(window,'nail_done_'..i).isVisible=true end
render(window,root..'/States/window_after.png');window:close()

local living=app.open(root..'/Sources/living.aseprite');local livingInfo=sceneInfo('living')
local broken=imageOf(assert(layerByName(living,'window_broken')))
for count=1,2 do put(living,livingInfo,'window_boards_'..count,W.living(broken,boards,count)) end
living:saveAs(root..'/Sources/living.aseprite')
layerByName(living,'glass').isVisible=true
for count=0,3 do
 layerByName(living,'window_broken').isVisible=count==0
 layerByName(living,'window_boards_1').isVisible=count==1
 layerByName(living,'window_boards_2').isVisible=count==2
 layerByName(living,'window_boarded').isVisible=count==3
 render(living,root..'/States/living_boards_'..count..'.png')
end
living:close()
f=assert(io.open(root..'/art-manifest.json','w'));f:write(json.encode(manifest));f:close()
print('WINDOW_PROGRESS_ART_OK: 2 room stages, 6 red markers, 6 raised nails; existing boards preserved.')
