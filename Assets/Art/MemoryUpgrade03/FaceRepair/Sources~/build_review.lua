local root=assert(_G.REPAIR_BUST_SOURCE_ROOT,
 'Missing FaceRepair source root; run this script with the adjacent aseprite_mcp_client.py.')
app.fs.makeAllDirectories(root..'Review')
local rig=dofile(root..'repair_rig.lua').new(root,'Native216')
local ids={'07_neutral','08_cat','09_heart'}
local sheet=Image(68*3,56,ColorMode.RGB);sheet:clear(app.pixelColor.rgba(0,0,0,255))
for i,id in ipairs(ids) do
 local im=rig.states[id].open
 im:saveAs(root..'Review/native_216_'..id..'.png')
 local crop=Image(68,56,ColorMode.RGB);crop:drawImage(im,Point(-70,-38));sheet:drawImage(crop,Point((i-1)*68,0))
end
local s=Sprite(sheet.width,sheet.height,ColorMode.RGB);s:newCel(s.layers[1],1,sheet,Point(0,0));app.activeSprite=s
app.command.SpriteSize{ui=false,width=816,height=224,method='nearest'};s:saveCopyAs(root..'Review/face_corrections.png');s:close()
local cat=rig.states['08_cat'].open;local scene=Sprite(216,162,ColorMode.RGB);scene:newCel(scene.layers[1],1,cat,Point(0,0));app.activeSprite=scene
app.command.SpriteSize{ui=false,width=864,height=648,method='nearest'};scene:saveCopyAs(root..'Review/native_216_cat_4x.png');scene:close();rig.master:close()
local f=assert(io.open(root..'Native216/Animation/manifest.json','r'));local manifest=json.decode(f:read('*a'));f:close()
local contacts=Image(432,324,ColorMode.RGB);contacts:clear(app.pixelColor.rgba(0,0,0,255))
for i,index in ipairs({1,2,3,5}) do
 local action=manifest.actions[index];local frame=action.frames[action.firstContactFrame+1]
 local im=Image{fromFile=root..'Native216/Animation/'..frame.png}
 contacts:drawImage(im,Point(((i-1)%2)*216,math.floor((i-1)/2)*162))
end
local contactSprite=Sprite(432,324,ColorMode.RGB);contactSprite:newCel(contactSprite.layers[1],1,contacts,Point(0,0));app.activeSprite=contactSprite
app.command.SpriteSize{ui=false,width=864,height=648,method='nearest'};contactSprite:saveCopyAs(root..'Review/single_hand_contact_sheet.png');contactSprite:close()
print('Native scene, face and single-hand contact reviews exported with nearest-neighbor scaling.')
