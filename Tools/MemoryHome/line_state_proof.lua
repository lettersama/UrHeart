local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local dest='H:/Unity6/MrTGame/Temp/MemoryHomeLineArt'
app.fs.makeAllDirectories(dest)
for _,id in ipairs({'window_boarded','workshop_robot','props_atlas_fixed'}) do
 local img=id=='props_atlas_fixed' and P.trace(Image{fromFile=P.root..'/LineSources/'..id..'.png'},432,432) or P.master(id)
 img:saveAs(dest..'/'..id..'.png')
end
print('Line-state native proofs exported.')
