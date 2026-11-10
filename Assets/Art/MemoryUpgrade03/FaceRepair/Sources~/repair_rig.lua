-- Fixed reference pixels plus independently editable local surface patches.
-- All image writes are native Aseprite Image operations.
local R={}
function R.new(root,folder)
 local master=app.open(root..folder..'/repair_bust.aseprite')
 local P=dofile(root..'animation_draw.lua').new(master.width,master.height)
 local pc=app.pixelColor
 local rig={P=P,master=master,root=root,folder=folder,layers={},parts={},states={}}
 for _,layer in ipairs(master.layers) do
  local im=P.image();local cel=layer:cel(1);if cel then im:drawImage(cel.image,cel.position) end
  rig.layers[#rig.layers+1]={name=layer.name,image=im}
 end
 rig.base=P.image();rig.base:drawSprite(master,1,Point(0,0))
 -- Black connected to the canvas boundary is outside the painted skin.
 -- Never allow a cosmetic patch to replace it with gray and grow the cheek.
 local exterior,queue={},{};local nextQ=1
 local function enqueue(x,y)
  if x<0 or y<0 or x>=P.W or y>=P.H then return end
  local index=y*P.W+x+1
  if not exterior[index] and rig.base:getPixel(x,y)==P.B then exterior[index]=true;queue[#queue+1]=index end
 end
 for x=0,P.W-1 do enqueue(x,0);enqueue(x,P.H-1) end
 for y=0,P.H-1 do enqueue(0,y);enqueue(P.W-1,y) end
 while nextQ<=#queue do
  local index=queue[nextQ]-1;nextQ=nextQ+1;local x,y=index%P.W,math.floor(index/P.W)
  for dy=-1,1 do for dx=-1,1 do if dx~=0 or dy~=0 then enqueue(x+dx,y+dy) end end end
 end
 rig.faceSurface=P.image()
 for y=0,P.H-1 do for x=0,P.W-1 do
  if pc.rgbaA(rig.layers[6].image:getPixel(x,y))>0 and not exterior[y*P.W+x+1] then rig.faceSurface:drawPixel(x,y,P.L) end
 end end
 local clean=P.image()
 P.rect(clean,86,44,4,4,P.L);P.rect(clean,112,46,5,5,P.L)
 rig.parts.clean=clean
 local areas={
  {{86,42},{91,41},{93,44},{91,47},{86,47},{85,45}},
  {{81,70},{86,69},{89,73},{87,76},{82,77},{80,74}},
  {{99,81},{104,80},{107,83},{104,85},{99,84}},
  {{116,69},{121,68},{123,71},{122,76},{117,79},{113,77},{114,73}},
 }
 local fill=P.image()
 for _,pts in ipairs(areas) do P.poly(fill,pts,P.L);P.speckle(fill,pts,4) end
 -- This remaining nick later becomes the cat tail / heart stitch.
 P.line(fill,118,73,115,76,P.B)
 rig.parts.filled=fill
 local powderL=P.image();for _,i in ipairs({1,2}) do P.poly(powderL,areas[i],P.L) end
 rig.parts.powder_left=powderL
 local powderR=P.image();for _,i in ipairs({3,4}) do P.poly(powderR,areas[i],P.L) end
 P.line(powderR,118,73,115,76,P.B);rig.parts.powder_right=powderR
 local brows=P.image()
 P.line(brows,80,48,84,47,P.B);P.line(brows,84,47,89,48,P.B)
 P.line(brows,111,50,115,49,P.B);P.line(brows,115,49,120,50,P.B)
 rig.parts.brows=brows
 -- The approved tiny mouth is preserved exactly; no lipstick replacement.
 local function glyph(im,x,y,rows)
  for j,row in ipairs(rows) do for i=1,#row do if row:sub(i,i)=='X' then P.dot(im,x+i-1,y+j-1,P.B) end end end
 end
 local cat=P.image();P.poly(cat,areas[4],P.L)
 glyph(cat,110,69,{
  '..X.......X..',
  '..XX.....XX..',
  '..X.XXXXX.X..',
  '..X.......X..',
  'XXX.X...X.XXX',
  '..X...X...X..',
  '...X.....X...',
  '....XXXXX....',
 })
 rig.parts.cat=cat
 local heart=P.image();P.poly(heart,areas[4],P.L)
 glyph(heart,112,69,{
  '.XX...XX.',
  'X..X.X..X',
  'X...X...X',
  'X.......X',
  '.X.....X.',
  '..X...X..',
  '...X.X...',
  '....X....',
 })
 rig.parts.heart=heart
 -- Apply the same surface restriction to every persistent cosmetic part.
 for _,part in pairs(rig.parts) do
  for y=0,P.H-1 do for x=0,P.W-1 do
   if pc.rgbaA(rig.faceSurface:getPixel(x,y))==0 then part:drawPixel(x,y,0) end
  end end
 end
 local definitions={
  {id='00_original',parts={},angle=0},
  {id='01_clean',parts={'clean'},angle=0},
  {id='02_filled',parts={'clean','filled'},angle=0},
  {id='03_powder_left',parts={'clean','filled','powder_left'},angle=0},
  {id='04_powder_both',parts={'clean','filled','powder_left','powder_right'},angle=0},
  {id='05_brows_uneven',parts={'clean','filled','powder_left','powder_right','brows'},angle=0},
  {id='06_brows_tilted',parts={'clean','filled','powder_left','powder_right','brows'},angle=-6},
  {id='07_neutral',parts={'clean','filled','powder_left','powder_right','brows'},angle=0},
  {id='08_cat',parts={'clean','filled','powder_left','powder_right','brows','cat'},angle=0},
  {id='09_heart',parts={'clean','filled','powder_left','powder_right','brows','heart'},angle=0},
 }
 rig.headMask=P.image()
 for y=0,P.H-1 do for x=0,P.W-1 do
  local lx,ly=x/P.S,y/P.S
  if pc.rgbaA(rig.layers[5].image:getPixel(x,y))>0 and not (ly>=86 and lx>=90 and lx<=115) and ly<92 then rig.headMask:drawPixel(x,y,P.L) end
 end end
 function rig.blink(im,phase)
  if not phase or phase==0 then return Image(im) end
  local out=Image(im)
  for i=7,8 do local eye=rig.layers[i].image
   for y=0,P.H-1 do for x=0,P.W-1 do if pc.rgbaA(eye:getPixel(x,y))>0 then out:drawPixel(x,y,P.L) end end end
  end
  if phase==1 then
   for i=7,8 do local eye=rig.layers[i].image
    for y=0,P.H-1 do for x=0,P.W-1 do local p=eye:getPixel(x,y);if pc.rgbaA(p)>0 and y>=(i==7 and 59 or 62)*P.S then out:drawPixel(x,y,p) end end end
   end
  else
   P.line(out,80,59,84,60,P.B);P.line(out,84,60,89,59,P.B)
   P.line(out,111,62,115,63,P.B);P.line(out,115,63,120,62,P.B)
  end
  return out
 end
 function rig.pose(im,angle)
  if not angle or angle==0 then return Image(im) end
  local result,head=Image(im),P.image()
  for y=0,P.H-1 do for x=0,P.W-1 do
   if pc.rgbaA(rig.headMask:getPixel(x,y))>0 then head:drawPixel(x,y,im:getPixel(x,y));result:drawPixel(x,y,P.B) end
  end end
  local under=P.image()
  P.poly(under,{{93,84},{109,84},{112,91},{107,95},{100,94},{94,90}},P.L)
  P.poly(under,{{79,87},{86,88},{89,101},{82,103}},P.L)
  P.poly(under,{{118,89},{124,89},{126,103},{121,103}},P.L)
  for y=0,P.H-1 do for x=0,P.W-1 do local p=under:getPixel(x,y)
   if pc.rgbaA(p)>0 and pc.rgbaA(rig.headMask:getPixel(x,y))>0 then result:drawPixel(x,y,p) end
  end end
  result:drawImage(P.rotate(head,angle,102,87));return result
 end
 for _,state in ipairs(definitions) do
  local upright=Image(rig.base);for _,part in ipairs(state.parts) do upright:drawImage(rig.parts[part]) end
  state.upright=upright;state.open=rig.pose(upright,state.angle);rig.states[state.id]=state
 end
 rig.definitions=definitions
 function rig.stateImage(id,phase)
  local s=assert(rig.states[id],id);return rig.pose(rig.blink(s.upright,phase),s.angle)
 end
 function rig.reveal(from,to,amount,side)
  if amount<=0 then return Image(from) end;if amount>=1 then return Image(to) end
  local im=Image(from)
  for y=0,P.H-1 do for x=0,P.W-1 do
   local a,b=from:getPixel(x,y),to:getPixel(x,y)
   if a~=b then
    local lx,ly=math.floor(x/P.S),math.floor(y/P.S)
    local rank=((lx*3+ly*5)%16)/16
    if side=='top' then rank=math.min(1,math.max(0,(ly-40)/47)) end
    if side=='fill' then
     local zone=ly<55 and 0 or (ly>=80 and 2 or (lx<100 and 1 or 3))
     rank=(zone+rank)/4
    end
    if side=='brows' then rank=((lx<100 and 0 or 1)+rank)/2 end
    if rank<amount then im:drawPixel(x,y,b) end
   end
  end end
  return im
 end
 function rig.hideTableProp(im,index)
  local out=Image(im);local mask=rig.layers[index].image;local floor=rig.layers[12].image
  for y=0,P.H-1 do for x=0,P.W-1 do
   if pc.rgbaA(mask:getPixel(x,y))>0 then local p=floor:getPixel(x,y);out:drawPixel(x,y,pc.rgbaA(p)>0 and p or P.B) end
  end end
  return out
 end
 return rig
end
return R
