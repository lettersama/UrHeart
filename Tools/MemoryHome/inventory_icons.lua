-- Inventory art drawn at native display size, inside Aseprite.
-- Deliberately simplified object views: silhouettes must read in a 60x36 slot.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local M={order={'robot','wood','hammer','nails','broom','cloth','tape','fuse'}}
local R,B,T=P.R,P.B,P.T
local function rect(im,x,y,w,h,c) P.poly(im,P.box(x,y,w,h),c or R) end
local function poly(im,p,c) P.poly(im,p,c or R) end
local function line(im,x,y,x2,y2,c) P.line(im,x,y,x2,y2,c or R) end
local painters={}

function painters.robot(im)
 -- Adult industrial android, adapted from the generated master at native size.
 local robot=Image{fromFile='H:/Unity6/MrTGame/Tools/MemoryHome/ArtSources/robot-icon-native.png'}
 assert(robot.width==34 and robot.height==36,'Wrong robot icon size')
 im:drawImage(robot,Point(3,-1))
end

function painters.wood(im)
 -- Long, overlapping boards with split ends and an explicit wood knot.
 poly(im,{4,12,31,5,34,7,35,14,8,22,6,20})
 line(im,8,18,32,12,B);line(im,8,18,8,21,B)
 line(im,12,13,22,10,B);line(im,26,9,31,8,B)
 line(im,29,14,33,13,B)
 -- Foreground plank crosses the rear one. Sparse grain follows its long axis.
 poly(im,{2,17,5,17,7,18,8,17,37,22,37,25,35,25,36,30,7,25,5,26,2,25})
 line(im,3,23,34,28,B);line(im,35,25,35,28,B)
 line(im,5,19,12,20,B);line(im,11,22,16,23,B)
 poly(im,{17,21,20,20,24,21,26,23,23,24,19,23},B)
 line(im,20,21,23,22)
 line(im,27,23,32,24,B)
end

function painters.hammer(im)
 -- A recognisable claw hammer with a broad striking face and split claw.
 poly(im,{21,10,26,13,14,32,11,34,7,32,7,29})
 line(im,21,15,10,30,B);line(im,10,31,12,32,B)
 poly(im,{12,3,18,2,24,5,26,7,31,9,35,14,35,18,31,14,28,13,30,17,32,19,28,18,24,14,20,11,15,10,12,8})
 poly(im,{11,3,15,3,19,9,17,12,13,11,9,6})
 line(im,16,4,21,7,B);line(im,18,9,22,11,B)
 line(im,29,12,33,16,B)
end

function painters.nails(im)
 -- Three individual flat heads and pointed shafts, rather than a tiny box.
 poly(im,{4,7,11,5,12,8,9,9,15,27,14,31,12,28,7,10,4,11})
 line(im,6,8,10,7,B)
 poly(im,{16,3,23,3,23,6,21,6,21,25,20,29,19,26,19,6,16,6})
 line(im,17,4,22,4,B)
 poly(im,{29,8,36,10,35,13,32,12,27,31,25,34,25,30,30,12,28,11})
 line(im,30,10,34,11,B)
end

function painters.broom(im)
 -- Long handle, binding and a broad fan of bristles.
 poly(im,{28,2,31,3,22,21,19,20})
 line(im,29,4,22,17,B)
 poly(im,{17,17,24,20,24,23,16,20})
 poly(im,{16,21,23,24,22,32,19,34,7,29,5,27,11,23})
 line(im,15,23,8,28,B);line(im,18,25,12,30,B)
 line(im,20,26,17,32,B)
end

function painters.cloth(im)
 -- A loose folded cleaning cloth with an overhanging corner.
 poly(im,{11,6,28,9,34,13,31,21,34,29,26,33,11,30,4,25,7,17})
 line(im,9,11,7,22,B);line(im,7,22,14,26,B)
 line(im,14,26,23,28,B);line(im,23,28,28,16,B)
 line(im,13,10,27,13,B)
 poly(im,{29,13,33,14,30,22,31,27,26,29,23,28})
 line(im,29,16,27,23,B)
 line(im,9,27,15,29,B);line(im,19,30,25,31,B)
 line(im,12,16,17,19,B);line(im,17,19,18,23,B)
end

function painters.tape(im)
 -- A thick tape roll, open core and a short unrolled tab.
 poly(im,{25,19,30,20,29,24,36,27,35,32,29,32,22,27,21,23})
 line(im,28,26,33,29,B)
 poly(im,{11,5,23,5,29,9,31,13,31,21,28,26,23,29,11,29,5,26,3,21,3,13,5,9})
 line(im,4,19,7,22,B);line(im,7,22,12,24,B)
 line(im,12,24,23,24,B);line(im,23,24,29,20,B)
 line(im,7,24,7,25,B);line(im,26,24,26,26,B)
 poly(im,{13,9,22,9,26,12,26,17,23,20,12,20,8,17,8,12},B)
 poly(im,{13,11,21,11,24,13,24,16,21,18,13,18,10,16,10,13},T)
 line(im,11,16,13,18);line(im,13,18,21,18);line(im,21,18,24,16)
end

function painters.fuse(im)
 -- Glass cartridge: two metal end caps, clear tube, single fuse filament.
 poly(im,{4,23,28,6,35,13,11,30},B)
 line(im,5,23,28,6);line(im,11,30,35,13)
 line(im,9,24,27,11)
 line(im,13,25,17,21);line(im,17,21,20,22)
 line(im,20,22,20,18);line(im,20,18,24,19);line(im,24,19,27,15)
 poly(im,{2,23,8,19,15,26,9,31,5,31,2,28})
 line(im,5,23,11,28,B);line(im,4,27,7,30,B)
 poly(im,{25,8,29,4,33,4,38,9,38,12,33,16})
 line(im,29,6,35,12,B);line(im,33,6,36,9,B)
end

function M.draw(id)
 assert(painters[id], 'Unknown inventory icon: '..tostring(id))
 local im=P.blank(40,36)
 painters[id](im)
 return im
end
function M.origin(index)
 return 20+((index-1)%4)*90,22+math.floor((index-1)/4)*85
end
return M
