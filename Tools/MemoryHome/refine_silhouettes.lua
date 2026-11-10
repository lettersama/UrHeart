-- Hand-traced native pixel contours. Run inside Aseprite, never in Unity.
local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local M={}
local shapes={
 hall={
  drawer={31,165,43,167,98,157,100,157,100,173,53,187,43,188,33,184,31,181},
  picture={26,40,69,59,60,97,17,86},
  picture_straight={20,47,65,47,65,93,19,93,19,52},
  letters={154,225,171,220,185,228,193,229,196,228,201,232,205,237,196,245,189,244,180,242,171,239,166,234,163,234},
  shoe={255,239,258,237,263,237,269,240,277,240,282,244,291,248,299,251,304,255,304,259,299,262,289,264,277,261,267,257,259,252,254,247},
 },
 living={
  cushion_down={44,167,101,150,103,150,124,175,125,178,74,196,72,197,64,191,54,181,47,172},
  cushion_up={51,177,59,169,72,160,77,157,88,157,94,156,103,158,117,161,125,166,129,170,127,175,119,185,108,200,105,203,97,202,83,198,67,193,55,190,52,186},
 },
 upstairs={portrait={180,91,207,91,207,121,180,121}},
 kitchen={
  cup={296,103,304,102,305,105,310,105,310,110,307,112,305,113,296,113},
  broom={421,72,425,72,427,75,408,171,411,174,409,176,404,176,406,186,408,188,408,199,415,232,403,232,389,230,373,226,381,215,391,193,396,181,399,176,398,173,402,169},
 },
 toolbox={
  hammer={178,84,186,83,188,89,192,90,199,87,201,81,214,77,220,77,230,80,240,87,247,94,254,101,256,105,248,99,237,94,229,92,224,93,224,97,218,96,221,109,227,132,236,160,243,171,244,176,236,178,229,180,225,176,219,153,209,116,206,107,204,101,203,97,188,97,188,101,180,101},
  nails={279,135,300,108,301,89,308,87,311,87,326,92,327,93,331,92,341,96,350,102,357,109,359,115,359,128,355,153,347,179,305,167,280,152},
  fuse={92,123,101,124,102,126,130,129,136,130,140,133,140,138,138,140,135,140,127,138,102,134,93,132,91,130},
 },
}
local holes={kitchen={cup={{306,107,308,107,308,109,306,110}}}}
local function trace(src,p,cutouts)
 local out=P.mask(src,p)
 -- Repair small breaks with one native ink pixel on the physical edge.
 -- Interior texture is retained; exterior background fragments are discarded.
 local x0,y0,x1,y1=P.bounds(out);local before=Image(out)
 for y=y0,y1 do for x=x0,x1 do if app.pixelColor.rgbaA(before:getPixel(x,y))>0 then
  for _,d in ipairs({{-1,0},{1,0},{0,-1},{0,1}}) do local nx,ny=x+d[1],y+d[2]
   if nx>=0 and nx<432 and ny>=0 and ny<318 and app.pixelColor.rgbaA(before:getPixel(nx,ny))==0 then out:putPixel(x,y,P.R);break end
  end
 end end end
 if cutouts then for _,h in ipairs(cutouts) do P.poly(out,h,P.T) end end
 return out
end
M.trace=trace
function M.apply(id,layers)
 for _,l in ipairs(layers) do
  if id=='entrance' and l.name=='handle' then
   local fixed=P.blank();fixed:drawImage(Image{fromFile=P.root..'/AuthoringOverrides/entrance_handle.png'},Point(268,92));l.image=fixed
  elseif shapes[id] and shapes[id][l.name] then l.image=trace(l.image,shapes[id][l.name],holes[id] and holes[id][l.name]) end
 end
 if id=='hall' then
  local down,up
  for _,l in ipairs(layers) do if l.name=='pot_down' then down=l.image elseif l.name=='pot_up' then up=l.image end end
  -- Leaves still follow the state swap; only the container receives feedback.
  layers[#layers+1]={name='pot_body_down',visible=true,image=trace(down,{55,233,58,229,63,226,71,224,80,223,87,225,92,228,96,233,98,240,98,247,95,252,90,256,85,258,78,258,70,254,64,250,59,244,56,239})}
  layers[#layers+1]={name='pot_body_up',visible=false,image=trace(up,{65,210,75,212,92,212,102,210,107,212,105,226,104,234,99,238,91,240,81,239,74,235,70,228,68,217})}
 end
 return layers
end
return M
