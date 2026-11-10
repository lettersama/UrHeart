-- Runtime authoring adapter: translate approved, native Aseprite hand cels.
-- Structural pixels are shared by every animation frame; no redrawing per tween.
local H={cache={}}
local root=assert(_G.REPAIR_BUST_SOURCE_ROOT,
 'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
function H.make(P,kind,tx,ty,contact)
 local folder=P.W==216 and 'Native216' or 'Detailed432'
 local key=folder..'/'..kind
 if not H.cache[key] then
  local layers={}
  for _,name in ipairs({'hand','tool','fingers'}) do layers[name]=Image{fromFile=root..folder..'/Hands/Layers/'..kind..'_'..name..'.png'} end
  H.cache[key]=layers
 end
 local dx=math.floor(tx*P.S+.5)-25*P.S
 local dy=math.floor(ty*P.S+.5)-25*P.S
 local result={shadow=P.image(),puff=P.image()}
 for _,name in ipairs({'hand','tool','fingers'}) do
  local im=P.image();im:drawImage(H.cache[key][name],Point(dx,dy));result[name]=im
 end
 return result
end
return H
