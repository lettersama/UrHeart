-- Native two-ink line-art adaptation. Executed in Aseprite.
local M={}
M.W=432;M.H=318
M.root='H:/Unity6/MrTGame/Assets/Art/SketchSrouce/MemoryHome'
M.B=app.pixelColor.rgba(0,0,0,255)
M.R=app.pixelColor.rgba(165,39,55,255)
M.T=app.pixelColor.rgba(0,0,0,0)
M.sourceFolder='LineSources'
function M.blank(w,h,c)
 local out=Image(w or M.W,h or M.H,ColorMode.RGB);out:clear(c or M.T);return out
end
function M.trace(src,w,h,inkThreshold)
 local out=M.blank(w,h,M.B)
 for y=0,h-1 do for x=0,w-1 do
  local x0,x1=math.floor(x*src.width/w),math.max(math.floor(x*src.width/w),math.floor((x+1)*src.width/w)-1)
  local y0,y1=math.floor(y*src.height/h),math.max(math.floor(y*src.height/h),math.floor((y+1)*src.height/h)-1)
  local sum,n=0,0
  for yy=y0,y1 do for xx=x0,x1 do sum=sum+app.pixelColor.rgbaR(src:getPixel(xx,yy));n=n+1 end end
  -- Retain ink strokes directly. No ordered dithering is added to the masters.
  if sum/n>=(inkThreshold or 46) then out:putPixel(x,y,M.R) end
 end end
 return out
end
M.cache={}
function M.master(id)
 if not M.cache[id] then M.cache[id]=M.trace(Image{fromFile=M.root..'/'..M.sourceFolder..'/'..id..'.png'},M.W,M.H) end
 return Image(M.cache[id])
end
function M.inside(x,y,p)
 local hit=false;local j=#p-1
 for i=1,#p,2 do
  if ((p[i+1]>y)~=(p[j+1]>y)) and x<(p[j]-p[i])*(y-p[i+1])/(p[j+1]-p[i+1])+p[i] then hit=not hit end
  j=i
 end
 return hit
end
function M.mask(src,p)
 local out=M.blank(src.width,src.height)
 for y=0,src.height-1 do for x=0,src.width-1 do if M.inside(x+.5,y+.5,p) then out:putPixel(x,y,src:getPixel(x,y)) end end end
 return out
end
function M.box(x,y,w,h) return {x,y,x+w,y,x+w,y+h,x,y+h} end
function M.px(img,x,y,c)
 x=math.floor(x+.5);y=math.floor(y+.5)
 if x>=0 and x<img.width and y>=0 and y<img.height then img:putPixel(x,y,c or M.R) end
end
function M.line(img,x0,y0,x1,y1,c)
 x0=math.floor(x0+.5);y0=math.floor(y0+.5);x1=math.floor(x1+.5);y1=math.floor(y1+.5)
 local dx,sx=math.abs(x1-x0),x0<x1 and 1 or -1
 local dy,sy=-math.abs(y1-y0),y0<y1 and 1 or -1;local e=dx+dy
 for guard=1,3000 do
  M.px(img,x0,y0,c);if x0==x1 and y0==y1 then break end
  local e2=2*e;if e2>=dy then e=e+dy;x0=x0+sx end;if e2<=dx then e=e+dx;y0=y0+sy end
 end
end
function M.poly(img,p,c)
 for y=0,img.height-1 do for x=0,img.width-1 do if M.inside(x+.5,y+.5,p) then img:putPixel(x,y,c or M.R) end end end
end
function M.bounds(img)
 local x0,y0,x1,y1=img.width,img.height,-1,-1
 for y=0,img.height-1 do for x=0,img.width-1 do if app.pixelColor.rgbaA(img:getPixel(x,y))>0 then
  x0=math.min(x0,x);y0=math.min(y0,y);x1=math.max(x1,x);y1=math.max(y1,y)
 end end end
 return x0,y0,x1,y1
end
-- Remove only the outside ink-black connected to the cell edge. Enclosed black
-- remains opaque so the prop occludes its background and keeps a clean contour.
function M.cutout(img)
 local w,h=img.width,img.height;local seen,q={},{};local function add(x,y)
  if x<0 or y<0 or x>=w or y>=h then return end
  local k=y*w+x+1
  if not seen[k] and img:getPixel(x,y)==M.B then seen[k]=true;q[#q+1]=k end
 end
 for x=0,w-1 do add(x,0);add(x,h-1) end;for y=0,h-1 do add(0,y);add(w-1,y) end
 local head=1
 while head<=#q do local k=q[head]-1;head=head+1;local x,y=k%w,math.floor(k/w);add(x-1,y);add(x+1,y);add(x,y-1);add(x,y+1) end
 for k in pairs(seen) do local n=k-1;img:putPixel(n%w,math.floor(n/w),M.T) end
 return img
end
function M.atlas(cell,w,h)
 if not M.atlasSource then
  local p=M.root..'/'..M.sourceFolder..'/props_atlas_fixed.png'
  M.atlasSource=Image{fromFile=p}
 end
 local src=M.atlasSource
 -- Hand-cut atlas regions retain overhanging plant leaves, the robot's head,
 -- and the full broom handle rather than assuming perfect generated grid cells.
 local regions={{15,15,286,290},{330,64,294,215},{643,52,289,223},{984,22,239,267},
 {17,366,286,214},{334,373,274,205},{620,381,347,210},{971,343,309,241},
 {17,632,290,262},{331,630,293,265},{615,580,351,326},{961,653,305,247},
 {17,938,285,230},{331,938,286,230},{650,910,212,315},{941,939,307,270}}
 local reg=regions[cell];local sx,sy=src.width/1280,src.height/1280
 local cw,ch=math.floor(reg[3]*sx),math.floor(reg[4]*sy)
 local tile=Image(src,Rectangle(math.floor(reg[1]*sx),math.floor(reg[2]*sy),cw,ch))
 -- Tighten the black cell around the actual object BEFORE sampling it natively.
 local ax,ay,bx,by=cw,ch,-1,-1
 for y=0,ch-1 do for x=0,cw-1 do if app.pixelColor.rgbaR(tile:getPixel(x,y))>65 then
  ax=math.min(ax,x);ay=math.min(ay,y);bx=math.max(bx,x);by=math.max(by,y)
 end end end
 local crop=Image(tile,Rectangle(math.max(0,ax-2),math.max(0,ay-2),math.min(cw-ax,bx-ax+5),math.min(ch-ay,by-ay+5)))
 local scale=math.min(w/crop.width,h/crop.height)
 local out=M.trace(crop,math.max(1,math.floor(crop.width*scale)),math.max(1,math.floor(crop.height*scale)),35)
 -- Authored solid silhouette masks: flood-filling native black regions would
 -- erase the object's dark faces and let the background show through them.
 local outlines={
  {0.02,.20,.21,.11,.28,.02,.46,0,.66,.07,.72,.19,.92,.28,1,.53,.91,.73,.77,.67,.77,.93,.64,1,.37,.98,.21,.86,.20,.70,0,.73,.08,.49},
  {0,.48,.10,.29,.30,.10,.66,0,.94,.15,1,.33,.89,.71,.67,1,.33,.86,.05,.70},
  {0,.31,.51,0,.98,.12,1,.67,.49,1,.02,.77},
  {.16,.04,.89,0,.91,.53,1,.91,.82,.91,.78,1,0,.96},
  {0,.46,.19,.33,.20,.10,.33,0,.68,.01,.83,.15,.78,.37,.97,.47,1,.62,.88,.85,.61,1,.22,.95,.04,.72},
  {.02,.26,.14,.08,.40,0,.68,.05,.80,.21,.79,.50,.98,.69,1,.94,.78,1,.56,.81,.21,.73,0,.55},
  {0,.19,.12,.02,.24,0,.31,.18,.51,.15,.76,.35,.92,.55,1,.53,1,.87,.94,1,.64,.80,.39,.62,.21,.49,.04,.42},
  {0,.40,.69,0,.98,.07,1,.50,.29,1,.10,.87},
  {0,.11,.14,.02,.36,0,.48,.08,.35,.15,.27,.26,.99,.83,1,.95,.89,1,.17,.37,.05,.34},
  {0,.28,.18,.25,.14,.10,.58,0,.64,.23,.80,.18,1,.32,.92,.58,.77,.90,.47,1,.10,.69},
  {0,.84,.19,.54,.39,.40,.42,.48,.94,0,1,.05,.48,.53,.55,.60,.40,.90,.20,1},
  {0,.40,.24,.04,.66,0,.96,.17,1,.50,.77,1,.14,.80},
  {0,.70,.76,0,.96,.04,1,.23,.25,1,.07,.95},
  {0,.70,.76,0,.96,.04,1,.23,.25,1,.07,.95},
  {.35,.01,.91,0,.86,.47,1,.61,.91,.96,.83,.99,.78,.69,.42,.71,.42,1,.33,1,.31,.70,.14,.60,.13,.96,.04,.96,0,.49,.31,.39},
  {0,.20,.67,0,.72,.07,.34,.31,.76,.40,.93,.31,1,.36,.97,.44,.70,.50,.98,.86,.97,1,.66,.95,.36,.70,.04,.63}
 }
 local poly={};for i,v in ipairs(outlines[cell]) do poly[i]=v*(i%2==1 and out.width or out.height) end
 local masked=M.mask(out,poly)
 -- Keep the chair's open back and spaces between its legs transparent.
 if cell==15 then
  for y=0,masked.height-1 do for x=0,masked.width-1 do
   local u,v=x/masked.width,y/masked.height
   local gap=(v>.12 and v<.38 and u>.44 and u<.77) or (v>.74 and u>.46 and u<.70)
   if gap and masked:getPixel(x,y)==M.B then masked:putPixel(x,y,M.T) end
  end end
 end
 return masked
end
return M
