-- Deterministic additions to the existing Aseprite window artwork.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local M={}
function M.nailPosition(i)
 local rows={121,136,152,173,181,203}
 return i%2==0 and 118 or 332,rows[i+1]
end
function M.marker(i)
 local x,y=M.nailPosition(i);local img=P.blank()
 local outline={x-2,y-5,x+2,y-5,x+5,y-2,x+5,y+2,x+2,y+5,x-2,y+5,x-5,y+2,x-5,y-2}
 P.poly(img,outline,P.B)
 for j=1,#outline,2 do local n=(j+1)%#outline+1;P.line(img,outline[j],outline[j+1],outline[n],outline[n+1],P.R) end
 return img
end
function M.raisedNail(i)
 local x,y=M.nailPosition(i);local img=P.blank()
 -- The head and exposed shaft stand above the marked point on the plank.
 P.poly(img,{x-5,y-9,x+2,y-10,x+5,y-6,x+3,y-3,x+2,y+2,x-2,y+3,x-3,y-4,x-5,y-5},P.B)
 P.poly(img,{x-4,y-8,x+1,y-9,x+4,y-6,x+1,y-4,x-4,y-5},P.R)
 P.line(img,x-1,y-4,x,y+1,P.R);P.line(img,x,y-4,x+1,y+1,P.R)
 P.line(img,x-2,y-7,x+1,y-7,P.B)
 return img
end
function M.living(broken,boards,count)
 local full=P.blank()
 for i=1,count do full:drawImage(boards[i],Point(0,0)) end
 local result=Image(broken)
 -- Preserve the original close-up-to-room projection and palette.
 for y=0,111 do for x=0,249 do local c=full:getPixel(x+98,y+107)
  if app.pixelColor.rgbaA(c)>0 then P.px(result,313+math.floor(x*86/250),78+math.floor(y*62/112),c) end
 end end
 return result
end
return M
