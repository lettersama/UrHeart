local root=assert(_G.REPAIR_BUST_SOURCE_ROOT,
 'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
local actions=dofile(root..'action_timing.lua')
local pc=app.pixelColor
local function dir(path) app.fs.makeAllDirectories(path);return path..'/' end
local function writeJson(path,value) local f=assert(io.open(path,'w'));f:write(json.encode(value));f:close() end
local function onceGif(path)
 local f=assert(io.open(path,'rb'));local data=f:read('*a');f:close()
 local at=data:find(string.char(33,255,11)..'NETSCAPE2.0',1,true)
 if at then assert(data:byte(at+14)==3 and data:byte(at+18)==0,'Unexpected GIF repeat block');data=data:sub(1,at-1)..data:sub(at+19) end
 f=assert(io.open(path,'wb'));f:write(data);f:close()
end
local function setPalette(sprite)
 local pal=Palette(3);pal:setColor(0,Color{r=0,g=0,b=0,a=0});pal:setColor(1,Color{r=0,g=0,b=0,a=255});pal:setColor(2,Color{r=135,g=130,b=126,a=255});sprite:setPalette(pal)
end
local function exportSprite(sprite,path,loop)
 sprite:saveAs(path..'.aseprite');sprite:saveCopyAs(path..'.gif');if not loop then onceGif(path..'.gif') end
 app.activeSprite=sprite;app.command.SpriteSize{ui=false,width=864,height=648,method='nearest'}
 sprite:saveCopyAs(path..'_preview.gif');if not loop then onceGif(path..'_preview.gif') end
 sprite:close()
end
local function sampleKeys(keys)
 local frames={}
 for i=1,#keys-1 do
  local a,b=keys[i],keys[i+1];local steps=math.max(1,math.ceil((b.t-a.t)/.10))
  for j=0,steps-1 do
   local u=j/steps;local f={time=a.t+(b.t-a.t)*u,duration=(b.t-a.t)/steps,contact=a.contact,fx=a.fx}
   for _,name in ipairs({'p','angle'}) do f[name]=a[name]+(b[name]-a[name])*u end
   if a.x then f.x=b.x and a.x+(b.x-a.x)*u or a.x;f.y=b.y and a.y+(b.y-a.y)*u or a.y end
   frames[#frames+1]=f
  end
 end
 local a=keys[#keys];frames[#frames+1]={time=a.t,duration=.20,p=a.p,angle=a.angle}
 return frames
end
local reports={}
for _,folder in ipairs({'Native216'}) do
 local rig=dofile(root..'repair_rig.lua').new(root,folder);local P=rig.P
 local out=dir(root..folder..'/Animation');local stateDir=dir(out..'States');local partDir=dir(out..'PatchParts')
 rig.base:saveAs(root..folder..'/repair_bust.png')
 rig.faceSurface:saveAs(out..'face_surface_mask.png')
 local manifest={version=1,canvas={width=P.W,height=P.H,origin={x=0,y=0}},palette={'#000000','#87827e'},maxVisibleHands=1,
  handRendering={sizeMultiplier=2,style='artist-edited floating grip with a short cuff',nativeOutlinePx=1,layeredMaster='../Hands/floating_hands.aseprite',source='Exact user-edited static hand pixels',sourceSha256='73df3263189f7d57ab1551936585743372392cd889714bb0ee8f3d2286bbef6e'},
  master='../repair_bust.aseprite',base='../repair_bust.png',states={},actions={},
  playback={idle='base + cumulative state patch + idle frame delta',operation='freeze idle at phase 0; base + from-state patch + operation frame delta',finish='Atomically replace cumulative patch with destination patch and clear operation delta, then start destination idle.',cancel='Discard operation overlay; retain original persistent state.',branch='08_cat and 09_heart are exclusive alternatives from 07_neutral.',mouth='Approved reference mouth is preserved. Lipstick and lip-outline additions were removed after review.',alpha='0 or 255; replacement pixels use opaque black / warm gray; never additive blending.',timing='All frames use full canvas and fixed origin. Aseprite and JSON frame durations are milliseconds.',hand='At most one working hand. One static grip per tool; positions are tweened, with local surface patches and contact particles. No supporting hand. Table props stay in place.'}}
 local stateSheet=Image(P.W*5,P.H*2,ColorMode.RGB);stateSheet:clear(P.B)
 for name,part in pairs(rig.parts) do part:saveAs(partDir..name..'.png') end
 for si,state in ipairs(rig.definitions) do
  local sd=dir(stateDir..state.id);dir(sd..'Frames');dir(sd..'IdleDeltas')
  state.open:saveAs(sd..'open.png');P.delta(state.open,rig.base):saveAs(sd..'state_patch.png')
  stateSheet:drawImage(state.open,Point(((si-1)%5)*P.W,math.floor((si-1)/5)*P.H))
  local spr=Sprite(P.W,P.H,ColorMode.RGB);setPalette(spr);local layers={}
  for i,entry in ipairs(rig.layers) do local l=i==1 and spr.layers[1] or spr:newLayer();l.name=entry.name;layers[#layers+1]={layer=l,image=entry.image} end
  for _,name in ipairs(state.parts) do local l=spr:newLayer();l.name='Surface / '..name;layers[#layers+1]={layer=l,image=rig.parts[name]} end
  if state.angle~=0 then local l=spr:newLayer();l.name='Head pose / neck connection';layers[#layers+1]={layer=l,image=P.delta(state.open,state.upright)} end
  local eyes=spr:newLayer();eyes.name='Idle / eyelid replacement'
  local phases,times={0,1,2,1,0},{2200,80,120,80,2700};local m={id=state.id,angle=state.angle,parts=state.parts,open='States/'..state.id..'/open.png',patch='States/'..state.id..'/state_patch.png',aseprite='States/'..state.id..'/idle.aseprite',gif='States/'..state.id..'/idle.gif',loop=true,frames={}}
  for i,phase in ipairs(phases) do
   if i>1 then spr:newEmptyFrame() end;spr.frames[i].duration=times[i]/1000
   for _,l in ipairs(layers) do spr:newCel(l.layer,i,l.image,Point(0,0)) end
   local full=rig.stateImage(state.id,phase);local delta=P.delta(full,state.open);spr:newCel(eyes,i,delta,Point(0,0))
   local frameName=string.format('%02d.png',i-1);full:saveAs(sd..'Frames/'..frameName);delta:saveAs(sd..'IdleDeltas/'..frameName)
   local check=P.image();check:drawSprite(spr,i,Point(0,0));assert(P.same(check,full)==0,'Idle composition mismatch')
   m.frames[#m.frames+1]={durationMs=times[i],png='States/'..state.id..'/Frames/'..frameName,delta='States/'..state.id..'/IdleDeltas/'..frameName,phase=phase}
  end
  spr:newTag(1,5).name='idle_loop';exportSprite(spr,sd..'idle',true);manifest.states[#manifest.states+1]=m
 end
 stateSheet:saveAs(out..'state_sheet.png')
 local actionDir=dir(out..'Actions');local actionSheet=Image(P.W*3,P.H*3,ColorMode.RGB);actionSheet:clear(P.B)
 local checks={}
 for ai,action in ipairs(actions) do
  local ad=dir(actionDir..action.id);dir(ad..'Frames');dir(ad..'Deltas')
  local from,to=rig.states[action.from],rig.states[action.to];local samples=sampleKeys(action.keys)
  local sprite=Sprite(P.W,P.H,ColorMode.RGB);setPalette(sprite)
  local names={'00 From-state composition','01 Local surface and pose progression','02 Contact shadow','03 Floating hand and short cuff','04 Tool','05 Grip fingers','06 Contact particles'}
  local layers={};for i,name in ipairs(names) do local l=i==1 and sprite.layers[1] or sprite:newLayer();l.name=name;layers[i]=l end
  local m={id=action.id,from=action.from,to=action.to,kind=action.kind,loop=false,aseprite='Actions/'..action.id..'/action.aseprite',gif='Actions/'..action.id..'/action.gif',resultPatch='Actions/'..action.id..'/result_patch.png',tweenKeys=action.keys,frames={}}
  local first,last,firstContact,frameMs
  for fi,f in ipairs(samples) do
   if fi>1 then sprite:newEmptyFrame() end
   -- GIF stores centiseconds. Quantize boundaries, preserving whole-track time.
   local nextTime=fi<#samples and samples[fi+1].time or f.time+f.duration
   frameMs=math.max(10,(math.floor(nextTime*100+.5)-math.floor(f.time*100+.5))*10);sprite.frames[fi].duration=frameMs/1000
   local subject
   if action.reveal=='pose' then subject=rig.pose(from.upright,f.angle)
   else subject=rig.reveal(from.open,to.open,f.p,action.reveal) end
   if fi==1 then subject=Image(from.open) elseif fi==#samples then subject=Image(to.open) end
   local hand,tool,fingers,shadow,particles=P.image(),P.image(),P.image(),P.image(),P.image()
   if f.x and action.kind~='none' then
    local h=P.toolHand(action.kind,f.x,f.y,f.contact);hand=h.hand;shadow=h.shadow
    tool=h.tool;fingers=h.fingers
    if f.fx>0 then
     local phase=math.floor(f.time*20)%7
     local count=action.kind=='puff' and 10 or (action.kind=='cloth' and 5 or 3)
     for n=1,count do
      local side=n%2==0 and 1 or -1
      local spread=action.kind=='puff' and 16 or (action.kind=='cloth' and 12 or 4)
      local dx=side*(spread+(n*3+phase)%8);local dy=((n*5+phase*2)%15)-10
      P.dot(particles,f.x+dx,f.y+dy,P.L,n%4==0 and 2 or 1)
      if action.kind=='cloth' then P.line(particles,f.x+dx,f.y+dy,f.x+dx+side*3,f.y+dy-1,P.L) end
     end
    end
   end
   local images={from.open,P.delta(subject,from.open),shadow,hand,tool,fingers,particles}
   local full=P.compose(images)
   for i,im in ipairs(images) do sprite:newCel(layers[i],fi,im,Point(0,0)) end
   local name=string.format('%03d.png',fi-1);full:saveAs(ad..'Frames/'..name);P.delta(full,from.open):saveAs(ad..'Deltas/'..name)
   local shift=f.x and action.kind~='none' and {x=math.floor(f.x*P.S+.5)-25*P.S,y=math.floor(f.y*P.S+.5)-25*P.S} or nil
   m.frames[#m.frames+1]={durationMs=frameMs,png='Actions/'..action.id..'/Frames/'..name,delta='Actions/'..action.id..'/Deltas/'..name,progress=f.p,contact=f.contact or false,handTranslation=shift}
   if not first then first=full end;last=full
   if f.contact and not firstContact then firstContact=fi end
   if fi==math.floor(#samples*.55) then actionSheet:drawImage(full,Point(((ai-1)%3)*P.W,math.floor((ai-1)/3)*P.H)) end
  end
  local startError,endError=P.same(first,from.open),P.same(last,to.open)
  assert(startError==0 and endError==0,'Action endpoint discontinuity: '..action.id)
  P.delta(to.open,from.open):saveAs(ad..'result_patch.png')
  sprite:newTag(1,#samples).name='play_once__commit_'..action.to
  m.durationMs=0;for _,f in ipairs(m.frames) do m.durationMs=m.durationMs+f.durationMs end
  m.commitFrame=#samples-1;m.firstContactFrame=firstContact and firstContact-1 or -1
  exportSprite(sprite,ad..'action',false)
  manifest.actions[#manifest.actions+1]=m;checks[#checks+1]={id=action.id,frames=#samples,startPixelDifferences=startError,endPixelDifferences=endError,durationMs=m.durationMs}
  print(folder..' '..action.id..': '..#samples..' frames; '..m.durationMs..' ms; matching start/end states.')
 end
 actionSheet:saveAs(out..'action_sheet.png');writeJson(out..'manifest.json',manifest)
 writeJson(out..'validation.json',{status='PASS',states=#manifest.states,actions=checks,patchComposition='checked for every idle and action endpoint',palette='two opaque colors and alpha 0/255'})
 reports[#reports+1]={folder=folder,states=#manifest.states,actions=#manifest.actions};rig.master:close()
end
writeJson(root..'animation_build.json',{status='PASS',versions=reports,method='Aseprite MCP native layered drawing / fixed hand poses / tweened position / local contact effects'})
print('All state idles, operations, cumulative patches, incremental result patches and metadata exported.')
