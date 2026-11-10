-- WORLD OF HORROR inspired line-art redraw: imagegen line masters, native
-- two-ink contours, silhouette masks, restored backgrounds and state layers.
-- All raster operations in this file run inside Aseprite via its MCP server.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local windowProgress=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/window_progress_art.lua')
local B,R,T=P.B,P.R,P.T
local navigationGray=app.pixelColor.rgba(135,130,123,255)
local root=P.root
local manifest={};local current,original,background,layers
app.fs.makeAllDirectories(root..'/Sources');app.fs.makeAllDirectories(root..'/Sprites')
app.fs.makeAllDirectories(root..'/States')
local function begin(id,clean)
 current=id;original=id=='icons' and P.blank(432,318,B) or P.master(id)
 background=clean and P.master(clean) or Image(original)
 layers={{name='Background',image=background,visible=true}}
end
local function add(name,img,visible)
 layers[#layers+1]={name=name,image=img,visible=visible~=false};return img
end
local function cut(name,p,visible,source) return add(name,P.mask(source or original,p),visible) end
local function patch(src,p) background:drawImage(P.mask(P.master(src),p),Point(0,0)) end
local function prop(name,cell,x,y,w,h,visible)
 local img=P.blank();img:drawImage(P.atlas(cell,w,h),Point(x,y));return add(name,img,visible)
end
local function save()
 dofile('H:/Unity6/MrTGame/Tools/MemoryHome/refine_silhouettes.lua').apply(current,layers)
 local spr=Sprite(432,318,ColorMode.RGB)
 local pal=Palette(current=='icons' and 3 or 2);pal:setColor(0,Color{r=0,g=0,b=0,a=255});pal:setColor(1,Color{r=165,g=39,b=55,a=255})
 if current=='icons' then pal:setColor(2,Color{r=135,g=130,b=123,a=255}) end
 spr:setPalette(pal)
 local info={id=current,layers={}}
 for i,l in ipairs(layers) do
  local sl=i==1 and spr.layers[1] or spr:newLayer();sl.name=l.name;sl.isVisible=l.visible
  if i==1 then sl:cel(1).image=l.image else spr:newCel(sl,1,l.image,Point(0,0)) end
  local x0,y0,x1,y1=P.bounds(l.image)
  assert(x1>=0,'Empty layer '..current..'/'..l.name)
  if i>1 then x0=math.max(0,x0-2);y0=math.max(0,y0-2);x1=math.min(431,x1+2);y1=math.min(317,y1+2) end
  local crop=Image(l.image,Rectangle(x0,y0,x1-x0+1,y1-y0+1))
  local file=current..'_'..l.name..'.png';crop:saveAs(root..'/Sprites/'..file)
  info.layers[#info.layers+1]={name=l.name,file=file,x=x0,y=y0,w=crop.width,h=crop.height,visible=l.visible}
 end
 spr:saveAs(root..'/Sources/'..current..'.aseprite')
 local img=P.blank(432,318,B);img:drawSprite(spr,1);img:saveAs(root..'/'..current..'.png')
 local red=0;for it in img:pixels() do if it()==R then red=red+1 end end;info.redCoverage=red/(432*318)
 manifest[#manifest+1]=info
 -- Alternate preview uses actual layer conditions, not an independently painted scene.
 local states={hall={pot_up=true,pot_body_up=true,pot_body_down=false,picture_straight=true,picture_fix_patch=true,pot_down=false,pot_soil=false,picture=false},
 kitchen={chair_up=true,chair_down=false,broom=false},living={window_broken=false,window_boarded=true,cushion_down=false,cushion_up=true,glass=false},
 workshop={robot=true,bench=false,planks=false},bedroom={tape=false},window={},
 wiring={power=false,power_off=true,fuse=false,fuse_fixed=true,wire_0=true,wire_1=true,wire_2=true,wire_3=true},
 table={books=false,books_stacked=true,photo=false,photo_up=true},sink={dishes=false,dishes_clean=true,cloth=false},toolbox={hammer=false,nails=false,fuse=false}}
 if current=='window' then for i=0,2 do states.window['board_'..i]=true end;for i=0,5 do states.window['nail_'..i]=false;states.window['nail_done_'..i]=true end end
 if states[current] then
  for _,l in ipairs(spr.layers) do local v=states[current][l.name];if v~=nil then l.isVisible=v end end
  local state=P.blank(432,318,B);state:drawSprite(spr,1);state:saveAs(root..'/States/'..current..'_after.png')
 end
 spr:close();print(current..': '..#layers..' layers, '..string.format('%.1f',info.redCoverage*100)..'% red')
end

begin('entrance')
cut('handle',{294,94,307,95,312,101,311,115,304,120,304,147,295,149,289,144,290,122,272,120,270,115,274,112,289,112,289,101})
save()

begin('hall')
patch('hall_clean',{20,193,81,182,142,205,164,230,163,269,19,269})
cut('drawer',{30,164,101,156,102,179,38,190,29,185})
cut('picture',{20,36,72,49,65,99,14,86})
cut('picture_fix_patch',P.box(12,32,64,71),false,P.master('hall_clean'))
cut('picture_straight',{21,44,66,44,65,94,18,93},false,P.master('hall_clean'))
cut('letters',{153,225,173,220,186,228,198,227,205,239,194,247,173,241,163,236})
cut('pot_down',{30,219,50,207,76,204,76,195,90,196,103,208,131,210,146,219,157,240,152,252,125,249,107,241,96,260,69,257,60,249,41,254,28,243})
cut('pot_soil',{24,242,162,239,164,268,24,268})
prop('pot_up',1,56,172,70,94,false)
cut('shoe',{255,237,264,235,275,239,289,240,297,248,309,253,308,262,292,267,266,260,254,248})
save()

begin('kitchen')
patch('kitchen_clean',P.box(35,161,145,109));patch('kitchen_clean',P.box(363,69,69,173))
cut('chair_down',{42,179,46,178,82,184,87,180,129,168,143,170,149,178,159,219,154,234,103,254,97,251,90,225,81,220,53,242,50,240,77,213,66,210,61,207,75,206,71,192,46,186})
prop('chair_up',15,62,124,83,139,false)
cut('sink',{148,91,194,91,205,106,229,86,248,85,257,105,271,98,278,113,147,116})
cut('cup',{295,102,306,101,306,104,311,104,312,111,307,114,306,115,294,115})
cut('broom',{421,70,427,70,429,76,409,170,413,173,409,185,418,233,390,234,368,227,391,183,392,175,400,169,419,74})
cut('cupboard',{134,0,287,0,287,63,135,67})
save()

-- Individually masked planks retain sparse wood lines and the scene perspective.
local function board(index)
 local shapes={{109,108,344,122,344,150,109,134},
 {109,137,344,158,344,185,109,162},
 {109,167,343,190,343,217,109,194}}
 local p=shapes[index+1];local img=P.blank();P.poly(img,p,B)
 for j=1,#p,2 do local next=(j+1)%#p+1;P.line(img,p[j],p[j+1],p[next],p[next+1],R) end
 P.line(img,p[1]+1,p[2]+2,p[3]-1,p[4]+2,R)
 -- Redraw only a few long grain marks and one knot from the generated board.
 -- The wide black faces keep nails and hammer feedback distinct at native size.
 local function yAt(x,offset) return p[2]+(x-p[1])*(p[4]-p[2])/(p[3]-p[1])+offset end
 local knots={164,255,190};local k=knots[index+1];local ky=yAt(k,13)
 local ring={k-6,ky,k-2,ky-3,k+3,ky-3,k+6,ky,k+2,ky+4,k-3,ky+3}
 for j=1,#ring,2 do local next=(j+1)%#ring+1;P.line(img,ring[j],ring[j+1],ring[next],ring[next+1],R) end
 P.line(img,119,yAt(119,12),k-13,yAt(k-13,11),R)
 P.line(img,k-13,yAt(k-13,11),k-7,ky-4,R)
 P.line(img,k+8,ky-3,k+20,yAt(k+20,12),R)
 P.line(img,k+20,yAt(k+20,12),333,yAt(333,13),R)
 P.line(img,137,yAt(137,21),191,yAt(191,22),R)
 P.line(img,256,yAt(256,21),326,yAt(326,22),R)
 return img
end
local boards={};for i=0,2 do boards[i+1]=board(i) end
begin('living')
patch('living_clean',P.box(43,141,99,65))
local broken=P.mask(original,{315,78,398,75,398,140,315,141})
P.poly(broken,{316,84,323,91,318,94,325,102,321,108,317,103},R)
P.poly(broken,{390,79,398,78,398,106,394,99,390,99,394,92},R)
P.poly(broken,{370,139,380,129,390,139},R);P.poly(broken,{373,138,380,132,387,138},B)
add('window_broken',broken,false)
add('window_boards_1',windowProgress.living(broken,boards,1),false)
add('window_boards_2',windowProgress.living(broken,boards,2),false)
add('window_boarded',windowProgress.living(broken,boards,3),false)
cut('cushion_down',{43,163,93,144,104,148,126,173,124,181,71,198,57,193})
prop('cushion_up',2,50,153,83,52,false)
cut('table',{148,212,274,184,359,193,304,240,149,224})
local glass=P.blank()
for _,v in ipairs({{333,227,10,4},{371,226,7,7},{351,241,12,5},{383,247,9,4},{323,252,8,5},{363,263,6,4},{401,258,10,3},{346,260,4,3}}) do
 local x,y,w,h=v[1],v[2],v[3],v[4];P.poly(glass,{x,y,x+w,y+h,x-2,y+h+2},R);P.line(glass,x,y+2,x+w-3,y+h,B)
end
add('glass',glass,false);save()

begin('upstairs')
cut('workshop_door',{60,1,133,35,132,216,62,245})
cut('bedroom_door',{350,37,401,0,403,262,349,223})
cut('daughter_door',{220,79,280,80,280,196,219,197})
cut('portrait',P.box(179,90,30,39))
cut('lamp',{232,0,235,19,244,26,247,33,242,45,222,47,215,40,218,29,231,21})
save()

begin('workshop')
patch('workshop_clean',{16,46,68,49,86,235,16,240})
cut('bench',{139,130,244,113,416,163,352,190,139,139})
cut('robot',{179,120,183,114,193,111,202,113,206,119,218,116,234,115,245,120,273,123,283,127,310,132,324,137,363,147,374,142,383,142,387,148,388,163,382,171,359,169,339,165,313,158,289,154,287,157,278,159,268,154,238,150,213,144,205,137,195,136,185,134,179,128},false,P.master('workshop_robot'))
cut('planks',{17,75,34,75,36,101,40,97,46,97,40,57,56,55,86,227,74,231,68,226,63,230,55,233,45,236,36,231})
cut('toolbox',{79,168,101,161,102,158,119,157,121,162,142,163,141,196,104,211,79,199})
cut('parts',{194,67,209,63,227,64,230,70,239,64,258,65,260,71,278,65,290,70,289,87,201,94})
save()

begin('bedroom')
-- Restore the plain wall and tabletop line beneath the small tape roll.
for y=119,134 do for x=181,202 do background:putPixel(x,y,original:getPixel(203,y)) end end
cut('blanket',{0,137,27,120,101,143,116,175,116,211,129,240,115,269,0,270})
cut('pillow',{0,101,32,95,75,98,93,109,82,122,30,133,0,126})
cut('bedside',{137,137,182,129,214,137,207,210,141,214})
cut('tape',{181,122,188,117,196,119,200,124,198,130,185,133,181,130},false)
cut('photo',{156,107,174,106,180,130,154,132})
cut('wardrobe',{283,0,432,0,432,246,285,206})
save()

begin('daughter')
local daughter=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/daughter_background.lua')
background=daughter.draw();layers[1].image=background
add('handle',daughter.layer('handle'))
add('door_note',daughter.layer('door_note'))
cut('mat',{122,244,298,252,338,270, 90,270},true,background)
save()

begin('window')
cut('frame_target',{106,94,342,99,343,231,104,221})
for i=0,2 do add('board_'..i,board(i),false) end
for i=0,5 do
 local x,y=windowProgress.nailPosition(i)
 add('nail_marker_'..i,windowProgress.marker(i),false)
 add('nail_'..i,windowProgress.raisedNail(i),false)
 local done=P.blank();P.poly(done,{x-3,y-1,x-1,y-3,x+3,y-2,x+4,y+1,x+1,y+3,x-3,y+2},B)
 P.poly(done,{x-3,y-1,x-1,y-2,x+2,y-2,x+3,y,x,y+2,x-3,y+1},R);P.line(done,x-1,y,x+1,y,B)
 add('nail_done_'..i,done,false)
end
save()

begin('wiring')
local rows={56,96,136,175};local socketP={P.box(282,38,37,38),P.box(282,78,37,36),P.box(282,119,37,36),P.box(282,157,37,39)}
for _,p in ipairs(socketP) do P.poly(background,p,B) end
local mapping={2,0,3,1}
for i=0,3 do
 local y=rows[i+1]
 cut('plug_'..i,{94,y-10,110,y-7,135,y-14,159,y-13,168,y-5,168,y+8,160,y+15,141,y+11,126,y+10,110,y+5,94,y+4})
 local source=mapping[i+1]+1;local crop=P.mask(original,socketP[source]);local moved=P.blank()
 moved:drawImage(crop,Point(0,rows[i+1]-rows[source]));add('socket_'..i,moved)
 local target=1;for j=1,4 do if mapping[j]==i then target=j end end
 local ty=rows[target];local wire=P.blank();local pts={}
 for n=0,90 do local t=n/90;local a=1-t
  pts[#pts+1]={a*a*a*163+3*a*a*t*208+3*a*t*t*238+t*t*t*288,a*a*a*y+3*a*a*t*(y+18)+3*a*t*t*(ty+18)+t*t*t*ty}
 end
 for _,p in ipairs(pts) do for yy=-3,3 do for xx=-3,3 do if xx*xx+yy*yy<=10 then P.px(wire,p[1]+xx,p[2]+yy,B) end end end end
 for n=2,#pts do local a,b=pts[n-1],pts[n]
  P.line(wire,a[1],a[2]-2,b[1],b[2]-2,R)
  P.line(wire,a[1],a[2]+2,b[1],b[2]+2,R)
 end
 -- Continuous cable contours keep the four crossing paths easy to follow.
 add('wire_'..i,wire,false)
end
cut('power',P.box(252,197,52,49))
local off=P.mask(original,P.box(252,197,52,49))
P.poly(off,{266,218,280,215,297,243,278,245},B)
P.poly(off,{274,223,280,220,288,207,287,202,282,200,278,203,278,209},R)
P.line(off,281,204,282,213,B);add('power_off',off,false)
cut('fuse',P.box(120,207,94,30))
local fixed=P.mask(original,P.box(120,207,94,30));P.poly(fixed,P.box(143,215,50,13),B)
P.line(fixed,143,218,192,219,R);P.line(fixed,144,226,192,225,R);P.line(fixed,145,222,190,222,R)
for x=145,190,7 do P.px(fixed,x,216,R) end;add('fuse_fixed',fixed,false)
save()

begin('drawer')
cut('letters',{117, 70,194, 80,228,91,249,110,263,113,250,140,231,168,182,156,119,138,113,118})
cut('scratches',{292,89,310,93,351,107,350,125,300,110})
cut('button',{286,129,294,127,301,130,302,136,299,142,289,144,284,139})
save()

begin('table','table_clean')
cut('books',{44,113,57,103,58,99,113,72,147,80,153,93,150,104,146,108,137,115,142,116,151,121,158,119,180,122,213,129,222,142,207,192,135,176,128,175,54,166,49,164,82,131,66,138,45,122})
prop('books_stacked',3, 60,100,140,94,false)
cut('photo',{246,132,324,122,333,143,348,184,360,200,359,214,329,218,319,204,270,217})
prop('photo_up',4,268,117,88,105,false)
cut('cup',{371,129,392,123,410,129,413,146,421,147,420,164,407,169,405,179,376,175,369,151})
save()

begin('sink','sink_clean')
cut('dishes',{129,143,131,124,142,114,155,109,170,109,182,116,191,128,197,143,200,147,204,142,211,131,221,129,229,134,241,145,238,151,243,155,242,165,235,171,225,172,213,168,207,173,145,174,139,166,135,158})
prop('dishes_clean',5,346,135,75,69,false)
cut('cloth',{0,139,30,140,39,144,44,154,56,156,66,160,77,169,93,169,98,174,95,182,76,191,66,196,65,211,55,229,51,245,43,244,38,227,23,219,11,220,0,215})
cut('tap',{290,72,297, 70,309,78,308,85,303,86,302,101,296,112,289,107,286,98,290,85,284,82,285,76})
save()

begin('toolbox','toolbox_clean')
cut('hammer',{176,84,188,82,190,88,200,85,201,79,216,75,231,79,246,89,257,100,257,107,248,101,235,93,226,93,224,101,229,122,246,173,244,178,228,181,220,175,204,109,201,99,189,99,189,103,179,103})
cut('nails',{276,113,297,90,310,85,342,95,359,108,361,147,346,181,305,170,278,153})
cut('fuse',{91,122,102,123,135,128,141,132,141,139,137,142,128,140,91,133})
save()

begin('icons')
local inventoryIcons=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/inventory_icons.lua')
for i,id in ipairs(inventoryIcons.order) do
 local x,y=inventoryIcons.origin(i)
 local img=P.blank();img:drawImage(inventoryIcons.draw(id),Point(x,y));add(id,img)
end
for i,id in ipairs({'arrow_left','arrow_right','arrow_up','arrow_down'}) do
 local img=P.blank();local cx,cy=32+(i-1)*90,213
 local shape={-11,0,-2,-9,-2,-4,10,-4,10,4,-2,4,-2,9}
 local transformed={};for j=1,#shape,2 do local x,y=shape[j],shape[j+1]
  if i==2 then x=-x elseif i==3 then x,y=-y,x elseif i==4 then x,y=y,-x end
  transformed[#transformed+1]=cx+x;transformed[#transformed+1]=cy+y
 end
 P.poly(img,transformed,navigationGray);P.px(img,cx+4,cy+2,B);P.px(img,cx+5,cy+2,B);add(id,img)
end
save()
local f=assert(io.open(root..'/art-manifest.json','w'));f:write(json.encode(manifest));f:close()
print('ART_PRODUCTION_OK '..#manifest..' source documents')
