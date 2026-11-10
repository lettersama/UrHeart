local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
local ids={'entrance','hall','kitchen','living','upstairs','workshop','bedroom','daughter','window','wiring','drawer','table','sink','toolbox'}
local sheet=P.blank(432*4,318*4,P.B)
app.fs.makeAllDirectories('H:/Unity6/MrTGame/Temp/MemoryHomePaint')
for i,id in ipairs(ids) do
 local img=P.master(id);img:saveAs('H:/Unity6/MrTGame/Temp/MemoryHomePaint/'..id..'.png')
 sheet:drawImage(img,Point(((i-1)%4)*432,math.floor((i-1)/4)*318))
end
sheet:saveAs('H:/Unity6/MrTGame/Temp/MemoryHomePaint/native-drafts.png')
print('Native master studies exported.')
