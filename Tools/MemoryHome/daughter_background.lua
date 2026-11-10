-- Taller close view of the far upstairs door, with independent interaction art.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local M={}
local art='H:/Unity6/MrTGame/Tools/MemoryHome/ArtSources'
local parts={
 handle={file='daughter-handle-original.png',w=28,h=33,x=272,y=117},
 door_note={file='daughter-note-original.png',w=26,h=34,x=202,y=44},
}
local function resize(source,w,h)
 local scaled=P.blank(w,h)
 for y=0,h-1 do for x=0,w-1 do
  local sx=math.min(source.width-1,math.floor((x+.5)*source.width/w))
  local sy=math.min(source.height-1,math.floor((y+.5)*source.height/h))
  scaled:putPixel(x,y,source:getPixel(sx,sy))
 end end
 return scaled
end
function M.draw()
 local master=Image{fromFile=art..'/daughter-symmetric-generated.png'}
 local out=P.blank(432,318,P.B)
 out:drawImage(P.trace(master,432,270,46),Point(0,0))
 -- Rectify the generated joinery on the native grid: four identical 40x86
 -- panels, symmetric about x=246.5 and y=130.5 inside the same doorframe.
 local panel=resize(Image(out,Rectangle(201,32,41,99)),40,86)
 P.poly(out,P.box(194,21,106,220),P.B)
 for _,y in ipairs({34,142}) do for _,x in ipairs({202,252}) do
  out:drawImage(panel,Point(x,y))
 end end
 return out
end
function M.layer(name)
 local part=assert(parts[name],'Unknown daughter door layer: '..tostring(name))
 local source=Image{fromFile=art..'/'..part.file}
 -- Nearest-neighbour adaptation preserves the existing two-ink silhouette.
 local scaled=resize(source,part.w,part.h)
 local out=P.blank();out:drawImage(scaled,Point(part.x,part.y))
 return out
end
return M
