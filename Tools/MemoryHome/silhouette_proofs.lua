local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local root=PROOF_ROOT or P.root
local dest=PROOF_DEST or 'H:/Unity6/MrTGame/Temp/MemoryHomeSilhouettes'
app.fs.makeAllDirectories(dest)
local function read(path) local f=assert(io.open(path,'r'));local s=f:read('*a');f:close();return json.decode(s) end
local manifest=read(root..'/art-manifest.json')
local content=read('H:/Unity6/MrTGame/Tools/MemoryHome/content.json')
local defs={};for _,s in ipairs(content.scenes) do defs[s.sceneId]=s end
local W=app.pixelColor.rgba(255,255,255,255)
local C1=app.pixelColor.rgba(66,66,66,255);local C2=app.pixelColor.rgba(84,84,84,255)
local function hover(img)
 local out=Image(img)
 for y=0,img.height-1 do for x=0,img.width-1 do
  if app.pixelColor.rgbaA(img:getPixel(x,y))==0 then
   for _,d in ipairs({{-1,0},{1,0},{0,-1},{0,1}}) do local xx,yy=x+d[1],y+d[2]
    if xx>=0 and xx<img.width and yy>=0 and yy<img.height and app.pixelColor.rgbaA(img:getPixel(xx,yy))>0 then out:putPixel(x,y,W);break end
   end
  end
 end end
 return out
end
local function blit(out,img,x,y,scale)
 for yy=0,img.height*scale-1 do for xx=0,img.width*scale-1 do local c=img:getPixel(math.floor(xx/scale),math.floor(yy/scale));if app.pixelColor.rgbaA(c)>0 then out:putPixel(x+xx,y+yy,c) end end end
end
local index={}
for _,s in ipairs(manifest) do if defs[s.id] then
 local selected={};for _,h in ipairs(defs[s.id].hotspots) do for _,name in ipairs(h.art) do selected[name]=true end end
 local part={};for _,l in ipairs(s.layers) do if selected[l.name] then part[#part+1]=l end end
 local out=Image(864,math.ceil(#part/3)*220,ColorMode.RGB)
 for y=0,out.height-1 do for x=0,out.width-1 do out:putPixel(x,y,(math.floor(x/12)+math.floor(y/12))%2==0 and C1 or C2) end end
 local listing={}
 for i,l in ipairs(part) do
  local img=Image{fromFile=root..'/Sprites/'..l.file};local scale=math.min(4,math.max(1,math.floor(math.min(280/img.width,210/img.height))))
  local ox=((i-1)%3)*288+math.floor((288-img.width*scale)/2);local oy=math.floor((i-1)/3)*220+math.floor((220-img.height*scale)/2)
  blit(out,hover(img),ox,oy,scale);listing[#listing+1]={cell=i,layer=l.name,x=l.x,y=l.y,w=l.w,h=l.h}
 end
 out:saveAs(dest..'/'..s.id..'-objects.png');index[s.id]=listing
end end
local f=assert(io.open(dest..'/proof-index.json','w'));f:write(json.encode(index));f:close()
print('Interaction alpha proofs exported.')
