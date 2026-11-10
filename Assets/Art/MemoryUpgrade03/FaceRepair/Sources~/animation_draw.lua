-- Drawing primitives and hand key poses. Every raster write runs in Aseprite.
local D={}
function D.new(W,H)
 local pc=app.pixelColor;local P={W=W,H=H,S=W/216,B=pc.rgba(0,0,0,255),L=pc.rgba(135,130,126,255)}
 local function round(n) return math.floor(n+.5) end
 function P.image() local im=Image(W,H,ColorMode.RGB);im:clear(0);return im end
 function P.raw(im,x,y,c) x=round(x);y=round(y);if x>=0 and y>=0 and x<W and y<H then im:drawPixel(x,y,c) end end
 function P.dot(im,x,y,c,size)
  local xx,yy=round(x*P.S),round(y*P.S);local n=math.max(1,round((size or 1)*P.S))
  for dy=0,n-1 do for dx=0,n-1 do P.raw(im,xx+dx,yy+dy,c) end end
 end
 function P.line(im,x0,y0,x1,y1,c,width)
  x0=round(x0*P.S);y0=round(y0*P.S);x1=round(x1*P.S);y1=round(y1*P.S)
  local dx,sx=math.abs(x1-x0),x0<x1 and 1 or -1;local dy,sy=-math.abs(y1-y0),y0<y1 and 1 or -1;local er=dx+dy
  local n=math.max(1,round((width or 1)*P.S))
  while true do
   for yy=0,n-1 do for xx=0,n-1 do P.raw(im,x0+xx,y0+yy,c) end end
   if x0==x1 and y0==y1 then break end
   local e=2*er;if e>=dy then er=er+dy;x0=x0+sx end;if e<=dx then er=er+dx;y0=y0+sy end
  end
 end
 function P.poly(im,pts,c,outline)
  local v={};for _,p in ipairs(pts) do v[#v+1]={round(p[1]*P.S),round(p[2]*P.S)} end
  local ymin,ymax=H-1,0;for _,p in ipairs(v) do ymin=math.min(ymin,p[2]);ymax=math.max(ymax,p[2]) end
  for y=math.max(0,ymin),math.min(H-1,ymax) do
   local xs={};for i,a in ipairs(v) do local b=v[i%#v+1];if (a[2]<=y and b[2]>y) or (b[2]<=y and a[2]>y) then xs[#xs+1]=a[1]+(y-a[2])*(b[1]-a[1])/(b[2]-a[2]) end end
   table.sort(xs);for i=1,#xs-1,2 do for x=math.max(0,math.ceil(xs[i])),math.min(W-1,math.floor(xs[i+1])) do P.raw(im,x,y,c) end end
  end
  for i,a in ipairs(pts) do local b=pts[i%#pts+1];P.line(im,a[1],a[2],b[1],b[2],outline or c) end
 end
 function P.rect(im,x,y,w,h,c) P.poly(im,{{x,y},{x+w-1,y},{x+w-1,y+h-1},{x,y+h-1}},c) end
 function P.ellipse(im,x,y,rx,ry,c)
  for yy=round((y-ry)*P.S),round((y+ry)*P.S) do for xx=round((x-rx)*P.S),round((x+rx)*P.S) do
   if ((xx/P.S-x)/rx)^2+((yy/P.S-y)/ry)^2<=1 then P.raw(im,xx,yy,c) end
  end end
 end
 function P.offset(pts,x,y) local r={};for _,p in ipairs(pts) do r[#r+1]={p[1]+x,p[2]+y} end;return r end
 function P.speckle(im,pts,period)
  local m=P.image();P.poly(m,pts,P.L)
  for y=0,H-1 do for x=0,W-1 do if pc.rgbaA(m:getPixel(x,y))>0 and (math.floor(x/P.S)+2*math.floor(y/P.S))%period==0 then P.raw(im,x,y,P.B) end end end
 end
 function P.delta(actual,base)
  local out=P.image();for y=0,H-1 do for x=0,W-1 do local a=actual:getPixel(x,y);if a~=base:getPixel(x,y) then out:drawPixel(x,y,a) end end end;return out
 end
 function P.same(a,b) local n=0;for y=0,H-1 do for x=0,W-1 do if a:getPixel(x,y)~=b:getPixel(x,y) then n=n+1 end end end;return n end
 function P.compose(images) local im=P.image();for _,p in ipairs(images) do im:drawImage(p,Point(0,0)) end;return im end
 function P.rotate(im,degrees,pivotX,pivotY)
  if degrees==0 then return Image(im) end
  local result=P.image();local a=degrees*math.pi/180;local c,s=math.cos(a),math.sin(a);local cx,cy=pivotX*P.S,pivotY*P.S
  for y=0,H-1 do for x=0,W-1 do
   local dx,dy=x-cx,y-cy;local sx=round(cx+c*dx+s*dy);local sy=round(cy-s*dx+c*dy)
   if sx>=0 and sy>=0 and sx<W and sy<H then local v=im:getPixel(sx,sy);if pc.rgbaA(v)>0 then result:drawPixel(x,y,v) end end
  end end
  return result
 end
 local handModule
 function P.toolHand(kind,tx,ty,contact)
  local sourceRoot=assert(_G.REPAIR_BUST_SOURCE_ROOT,
   'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
  handModule=handModule or dofile(sourceRoot..'reference_hands.lua')
  return handModule.make(P,kind,tx,ty,contact)
 end
 return P
end
return D
