-- Draw the authored pixels with nearest-neighbour enlargement for review.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local I=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/inventory_icons.lua')
local out='H:/Unity6/MrTGame/Temp/MemoryHomeInventoryIcons'
app.fs.makeAllDirectories(out..'/draft')
local board=P.blank(4*228,2*240,P.B)
local before=P.blank(4*228,2*240,P.B)
local function paste(sheet,img,index)
 local col,row=(index-1)%4,math.floor((index-1)/4)
 local scale=5
 local x0=col*228+math.floor((228-img.width*scale)/2)
 local y0=row*240+12
 for y=0,img.height-1 do for x=0,img.width-1 do
  local c=img:getPixel(x,y)
  if app.pixelColor.rgbaA(c)>0 then
   for yy=0,scale-1 do for xx=0,scale-1 do sheet:putPixel(x0+x*scale+xx,y0+y*scale+yy,c) end end
  end
 end end
 sheet:drawImage(img,Point(col*228+math.floor((228-img.width)/2),row*240+204))
end
for index,id in ipairs(I.order) do
 local im=I.draw(id)
 im:saveAs(out..'/draft/icons_'..id..'.png')
 paste(board,im,index)
 local previous=out..'/before/icons_'..id..'.png'
 if app.fs.isFile(previous) then paste(before,Image{fromFile=previous},index) end
end
board:saveAs(out..'/draft/contact.png')
app.fs.makeAllDirectories('H:/Unity6/MrTGame/Docs/MemoryHome')
board:saveAs('H:/Unity6/MrTGame/Docs/MemoryHome/inventory-icons.png')
if app.fs.isDirectory(out..'/before') then before:saveAs(out..'/before/contact.png') end
print('Eight inventory icons rendered at native size and 5x.')
