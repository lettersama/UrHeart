local root=assert(_G.REPAIR_BUST_SOURCE_ROOT,
 'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
local reports={}
for _,folder in ipairs({'Native216'}) do
 local path=root..folder..'/Animation/'
 local f=assert(io.open(path..'manifest.json','r'));local manifest=json.decode(f:read('*a'));f:close()
 local P=dofile(root..'animation_draw.lua').new(manifest.canvas.width,manifest.canvas.height)
 local report={folder=folder,files=0,frames=0,compositePixelDifferences=0,durationMismatches=0,invalidLayerPixels=0,singleHandActions=0,artistHandPixelDifferences=0}
 local items={};for _,s in ipairs(manifest.states) do items[#items+1]=s end;for _,a in ipairs(manifest.actions) do items[#items+1]=a end
 for _,item in ipairs(items) do
  local s=app.open(path..item.aseprite);assert(#s.frames==#item.frames,item.id..' frame count')
  if item.from then
   local expected={'00 From-state composition','01 Local surface and pose progression','02 Contact shadow','03 Floating hand and short cuff','04 Tool','05 Grip fingers','06 Contact particles'}
   assert(#s.layers==#expected,item.id..' unexpected operation layers')
   for i,name in ipairs(expected) do assert(s.layers[i].name==name,item.id..' unexpected hand layer') end
   report.singleHandActions=report.singleHandActions+1
  end
  for i,frame in ipairs(item.frames) do
   local full=P.image();full:drawSprite(s,i,Point(0,0));local expected=Image{fromFile=path..frame.png}
   report.compositePixelDifferences=report.compositePixelDifferences+P.same(full,expected)
   if math.abs(s.frames[i].duration*1000-frame.durationMs)>.1 then report.durationMismatches=report.durationMismatches+1 end
   report.frames=report.frames+1
   if item.from then
    for handIndex,key in ipairs({'hand','tool','fingers'}) do
     local expectedHand=P.image()
     if frame.handTranslation then
      local original=Image{fromFile=root..folder..'/Hands/Layers/'..item.kind..'_'..key..'.png'}
      expectedHand:drawImage(original,Point(frame.handTranslation.x,frame.handTranslation.y))
     end
     local actualHand=P.image();local cel=s.layers[handIndex+3]:cel(i)
     if cel then actualHand:drawImage(cel.image,cel.position) end
     report.artistHandPixelDifferences=report.artistHandPixelDifferences+P.same(actualHand,expectedHand)
    end
   end
  end
  for _,cel in ipairs(s.cels) do
   for pixel in cel.image:pixels() do local p=pixel();if app.pixelColor.rgbaA(p)>0 and p~=P.B and p~=P.L then report.invalidLayerPixels=report.invalidLayerPixels+1 end end
  end
  report.files=report.files+1;s:close()
 end
 assert(report.compositePixelDifferences==0 and report.durationMismatches==0 and report.invalidLayerPixels==0 and report.artistHandPixelDifferences==0,'Reopened source mismatch')
 report.status='PASS';reports[#reports+1]=report
 print(folder..': reopened '..report.files..' sources / '..report.frames..' frames; pixels, timings and layers PASS.')
end
local f=assert(io.open(root..'verification_native.json','w'));f:write(json.encode({status='PASS',versions=reports}));f:close()
