print("ASEPRITE_LUA_READY")
local s=Sprite(432,318,ColorMode.RGB)
print(s.width,s.height,#s.cels,app.version)
s:close()
