local P=dofile('H:/Unity6/MrTGame/Tools/MemoryHome/paint_native.lua')
app.fs.makeAllDirectories('H:/Unity6/MrTGame/Temp/MemoryHomeLineArt')
for _,id in ipairs({'entrance','hall'}) do
 local img=P.master(id)
 img:saveAs('H:/Unity6/MrTGame/Temp/MemoryHomeLineArt/'..id..'.png')
end
print('Native line-art proofs exported through Aseprite MCP.')
