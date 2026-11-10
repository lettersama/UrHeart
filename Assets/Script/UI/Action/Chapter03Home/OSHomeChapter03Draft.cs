using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The chapter-3 house and yard from the deployment draft (2026-10-23),
/// one screen each. Positions are a starting point; the backend may keep this
/// in code or fill an OSHomeLayout asset with it (asset context menu) and edit.
/// Glyph/label keys follow os.home.&lt;room&gt;.&lt;thing&gt;.glyph / .label; when a key
/// is missing the Chinese text below is used.
/// </summary>
public static class OSHomeChapter03Draft
{
    const int T = OSHomeLayout.Tile;

    public static OSHomeLayout Create()
    {
        var layout = ScriptableObject.CreateInstance<OSHomeLayout>();
        layout.name = "Chapter03HomeDraft";
        Fill(layout);
        return layout;
    }

    public static void Fill(OSHomeLayout layout)
    {
        layout.firstRoom = "study";
        layout.firstTile = new Vector2Int(5, 7);
        layout.firstDongTile = new Vector2Int(6, 7);
        layout.rooms = new List<OSHomeRoom>();

        var study = Room(layout, "study", "主人的书房", 2, 2, 12, 8);
        Door(study, "study_door", 3, 9, "west", 3, 3, "书房门");
        Thing(study, "shelf", 4, 2, "书架", "书架");
        Thing(study, "desk", 8, 3, "书桌", "书桌");
        Thing(study, "lamp", 10, 3, "台灯", "台灯");
        Thing(study, "page", 9, 2, "纸", "书桌上那页");
        Thing(study, "dock", 2, 7, "充电座", "充电座");
        Thing(study, "curtain", 9, 1, "窗帘", "窗帘");
        Leak(study, "curtain", 9, 1, 2);
        Thing(study, "clock", 6, 1, "钟", "钟");
        Thing(study, "chair", 8, 4, "椅", "主人的椅子").style = OSHomeThingStyle.DashedEmpty;
        study.empties.Add(new Vector2Int(8, 4));
        Lamp(study, "lamp", OSHomeLightKind.Faulty, 10.5f, 3f, 30f, 2f); // Its text says it flickers.
        Foot(study, (3, 8), (3, 7));

        var west = Room(layout, "west", "左廊", 1, 3, 13, 5);
        Door(west, "bed_door", 0, 4, "bed", 13, 4, "主人卧室门", OSHomeInk.Yellow, locked: true);
        Door(west, "west_hall", 14, 4, "hall", 1, 4, "去大厅");
        Door(west, "west_study", 3, 2, "study", 3, 8, "书房门");
        Thing(west, "painting", 7, 2, "挂画", "挂画");
        Thing(west, "note", 1, 5, "纸", "门口地上的纸", walk: true);
        Strip(west, "bed_slit", new RectInt(1, 4 * T + 1, T - 2, T - 2), 3f); // Lights the whole 门.
        for (int x = 13; x >= 4; x--) Foot(west, (x, 4));
        Foot(west, (3, 3));

        var bath = Room(layout, "bath", "卫生间", 5, 4, 8, 6);
        Door(bath, "bath_door", 6, 7, "bed", 6, 1, "卧室", OSHomeInk.Yellow);
        var mirror = Thing(bath, "mirror", 5, 3, "镜柜", "镜柜");
        mirror.style = OSHomeThingStyle.WallMirror;
        mirror.width = 3; // The right edge is reachable beside the sink.
        Thing(bath, "sink", 5, 4, "水池", "水池").interactive = false;
        Thing(bath, "towel", 8, 3, "毛巾", "毛巾").interactive = false;
        Water(bath, (7, 5), (7, 6), (8, 6));

        var bed = Room(layout, "bed", "主人的卧室", 1, 1, 13, 8);
        Door(bed, "bed_west", 14, 4, "west", 1, 4, "去走廊", OSHomeInk.Yellow);
        Door(bed, "bed_bath", 6, 0, "bath", 6, 6, "卫生间", OSHomeInk.Yellow);
        Thing(bed, "lamp", 2, 2, "立灯", "立灯");
        Thing(bed, "bed", 8, 2, "大床", "大床").hitTiles.Add(new Vector2Int(8, 3));
        Thing(bed, "pills", 10, 2, "药瓶", "药瓶");
        Thing(bed, "curtain", 9, 0, "窗帘", "窗帘");
        Leak(bed, "curtain", 9, 0, 2);
        Thing(bed, "nightstand", 10, 3, "床头柜", "床头柜");
        Thing(bed, "letter", 10, 4, "信", "床头柜边的信", walk: true);
        Thing(bed, "wardrobe", 11, 7, "衣柜", "衣柜");
        bed.empties.Add(new Vector2Int(8, 3));
        Lamp(bed, "lamp", OSHomeLightKind.Steady, 2.5f, 2f, 34f, 6f);

        var hall = Room(layout, "hall", "大厅和玄关", 1, 1, 13, 8);
        Door(hall, "hall_west", 0, 4, "west", 13, 4, "去左廊");
        Door(hall, "hall_east", 14, 4, "east", 1, 4, "去右廊");
        Door(hall, "hall_kitchen", 7, 0, "kitchen", 7, 8, "厨房");
        Door(hall, "front_door", 7, 9, "yard", 7, 1, "大门", locked: true);
        Thing(hall, "curtain", 3, 0, "窗帘", "窗帘");
        Leak(hall, "curtain", 3, 0, 2);
        Thing(hall, "clock", 11, 0, "钟", "钟");
        Thing(hall, "photo", 1, 2, "相框", "相框");
        Thing(hall, "sofa", 9, 2, "沙发", "沙发").hitTiles.Add(new Vector2Int(11, 2));
        Thing(hall, "phone", 12, 3, "电话", "电话"); // Keep the east doorway's (13,4) arrival clear.
        Thing(hall, "lamp", 6, 4, "大灯", "大灯");
        Thing(hall, "table", 8, 6, "茶几", "茶几");
        Thing(hall, "paper", 10, 6, "纸", "茶几上的纸", walk: true);
        Thing(hall, "coat", 4, 8, "外套", "外套");
        Thing(hall, "shoes", 6, 8, "鞋", "鞋");
        Thing(hall, "umbrella", 9, 8, "伞", "伞");
        hall.empties.Add(new Vector2Int(11, 2));
        Lamp(hall, "lamp", OSHomeLightKind.Breathing, 6.5f, 4f, 52f, 8f);
        for (int x = 13; x >= 1; x--) Foot(hall, (x, 5));

        var kitchen = Room(layout, "kitchen", "厨房", 2, 2, 12, 8);
        Door(kitchen, "kitchen_hall", 7, 9, "hall", 7, 1, "大厅");
        Thing(kitchen, "calendar", 3, 1, "日历", "日历").approachDistance = 2;
        Thing(kitchen, "clock", 6, 1, "钟", "钟").approachDistance = 2;
        Thing(kitchen, "coffee_machine", 3, 2, "咖啡机", "咖啡机");
        Thing(kitchen, "coffee_jar", 6, 2, "罐", "咖啡罐");
        Thing(kitchen, "sink", 8, 2, "水槽", "水槽");
        Thing(kitchen, "fridge", 11, 2, "冰箱", "冰箱");
        Thing(kitchen, "sugar", 11, 4, "糖罐", "糖罐");
        Thing(kitchen, "table", 5, 5, "餐桌", "餐桌");
        Thing(kitchen, "cup", 7, 5, "杯子", "杯子");
        Thing(kitchen, "drawer", 3, 8, "抽屉", "抽屉");
        var screwdriver = Thing(kitchen, "screwdriver", 5, 8, "起子", "起子", OSHomeInk.Blue);
        screwdriver.hidden = true;
        kitchen.empties.Add(new Vector2Int(5, 6));
        Water(kitchen, (8, 3), (9, 3), (8, 4), (9, 4), (10, 4), (9, 5));
        Strip(kitchen, "fridge", new RectInt(11 * T + 2, 3 * T, 2 * T - 4, 1), 3f);

        var store = Room(layout, "store", "储物间", 1, 1, 9, 8);
        Door(store, "store_east", 4, 9, "east", 4, 3, "右廊");
        Thing(store, "boxes", 2, 2, "箱箱箱", "箱子");
        Thing(store, "old_leg", 7, 2, "旧腿", "旧腿");
        Thing(store, "tube", 4, 4, "灯管", "灯管");
        Thing(store, "toolbox", 2, 6, "工具箱", "工具箱");
        Thing(store, "makeup", 6, 6, "化妆包", "化妆包");
        Thing(store, "key", 7, 7, "钥匙", "钥匙", OSHomeInk.Yellow).hidden = true;
        Thing(store, "trail", 5, 8, "痕", "地上", walk: true);
        Lamp(store, "tube", OSHomeLightKind.Flicker, 4.5f, 4f, 36f, 4f);

        var east = Room(layout, "east", "右廊", 1, 3, 13, 5);
        Door(east, "east_hall", 0, 4, "hall", 13, 4, "去大厅");
        Door(east, "lab_door", 14, 4, "lab", 1, 4, "实验室门", locked: true);
        Door(east, "store_door", 4, 2, "store", 4, 8, "储物间门", locked: true);
        Door(east, "junk_door", 9, 6, "junk", 9, 1, "坏门");
        Door(east, "stuck_door", 11, 2, null, 11, 3, "卡死的门", locked: true);
        Thing(east, "keypad", 12, 3, "密码", "密码锁");
        Strip(east, "lab_slit", new RectInt(14 * T - 2, 4 * T + 2, 1, T - 4), 3f);
        for (int x = 13; x >= 1; x--) Foot(east, (x, 4));

        var junk = Room(layout, "junk", "杂物间", 4, 1, 12, 6);
        Door(junk, "junk_east", 9, 0, "east", 9, 5, "右廊");
        Thing(junk, "boxes", 5, 2, "纸箱", "纸箱");
        Thing(junk, "wheelchair", 10, 4, "轮椅", "轮椅");
        Thing(junk, "medicine", 5, 5, "药盒", "药盒");

        var lab = Room(layout, "lab", "实验室", 1, 1, 13, 8);
        Door(lab, "lab_east", 0, 4, "east", 13, 4, "右廊");
        Thing(lab, "or_lamp", 7, 2, "无影灯", "无影灯");
        Thing(lab, "bed", 6, 4, "床主人床", "床");
        Thing(lab, "mask", 2, 2, "脸模", "架子上的石膏");
        Thing(lab, "mask_note", 4, 2, "纸", "石膏下的纸");
        Thing(lab, "bench", 2, 7, "工作台", "工作台");
        Thing(lab, "log", 9, 7, "记录", "墙上的本子");
        Thing(lab, "pillow_note", 8, 5, "纸", "枕边的纸", walk: true);
        // pancake 10-27: the lamp over her bed is off; a desk lamp by the bench is the room's only light,
        // so the player goes down to the bench first and follows its blood trail up to the bed.
        // Touching the bed switches the bed lamp on (the director's lab_lamp_on).
        var benchLamp = Thing(lab, "bench_lamp", 1, 8, "台灯", "台灯"); // below-left of the bench (pancake 10-27)
        benchLamp.glyphKey = "os.home.study.lamp.glyph"; benchLamp.labelKey = "os.home.study.lamp.label";
        benchLamp.interactive = false;
        Blood(lab, (5, 7), (5, 6), (6, 6), (6, 5), (7, 5));
        // What she holds, beside the bed; it opens onto the hairclip and the envelope.
        Thing(lab, "box", 10, 4, "盒子", "小盒子", OSHomeInk.Red).hidden = true;
        Thing(lab, "hairclip", 10, 5, "发卡", "发卡", OSHomeInk.Red).hidden = true;
        Thing(lab, "letter", 10, 3, "信封", "信封", OSHomeInk.Red).hidden = true;
        Lamp(lab, "or_lamp", OSHomeLightKind.Steady, 8f, 4f, 36f, 1f).on = false;
        Lamp(lab, "bench_lamp", OSHomeLightKind.Steady, 1.5f, 8f, 40f, 6f);
        // Small breathing lights that call the player to whatever is next; off until the director needs one.
        Lamp(lab, "box_glow", OSHomeLightKind.Steady, 10.5f, 4f, 9f, 3f).on = false;
        Lamp(lab, "hairclip_glow", OSHomeLightKind.Steady, 10.5f, 5f, 9f, 3f).on = false;
        Lamp(lab, "letter_glow", OSHomeLightKind.Steady, 10.5f, 3f, 9f, 3f).on = false;

        var yard = Room(layout, "yard", "院子", 1, 1, 13, 8);
        yard.outdoors = true;
        yard.recordFootprints = true;
        yard.showVisitedMarkers = true;
        yard.boundaryGlyphKey = "os.home.yard.fence.glyph";
        yard.boundaryGlyph = "栏";
        yard.boundaryThingId = "yard_fence";
        // Out of the hall's front door: the house wall along the top, the gate at the bottom,
        // a stone path straight between them. 主人's window is top left, half hidden behind
        // the clothesline and the tree; the vegetable plot is on the right. Puddles everywhere.
        yard.houseWallTop = true;
        Door(yard, "yard_house", 7, 0, "hall", 7, 8, "回屋").labelKey = "os.home.hall.title";
        Thing(yard, "bedwindow", 2, 0, "窗", "主人卧室的窗户").width = 2;
        Thing(yard, "clothes", 1, 2, "衣绳", "晒衣绳").width = 3;
        Thing(yard, "tree", 5, 2, "树", "树");
        Thing(yard, "fence", 0, 6, "栏", "围栏");
        Thing(yard, "gate", 7, 9, "门", "院门").glyphKey = "os.home.glyph.door";
        Thing(yard, "seedlings", 10, 4, "菜苗", "菜苗");
        for (int y = 1; y <= 8; y++) yard.stones.Add(new Vector2Int(7, y));
        for (int y = 2; y <= 6; y++)
            for (int x = 10; x <= 13; x++) yard.garden.Add(new Vector2Int(x, y));
        Water(yard, (6, 3), (6, 4), (7, 6), (8, 5), (8, 6), (9, 6), (3, 5), (4, 5), (4, 6), (2, 7),
            (5, 8), (9, 2), (11, 7), (12, 7), (12, 8), (1, 4));
        Strip(yard, "bedwindow", new RectInt(2 * T + 1, T - 2, 2 * T - 2, 1), 4f);
    }

    static OSHomeRoom Room(OSHomeLayout layout, string id, string title, int x0, int y0, int x1, int y1)
    {
        var room = new OSHomeRoom
        {
            id = id, title = title, titleKey = "os.home." + id + ".title",
            floor = new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1)
        };
        layout.rooms.Add(room);
        return room;
    }

    static OSHomeDoor Door(OSHomeRoom room, string id, int x, int y, string target, int ax, int ay,
        string label, OSHomeInk ink = OSHomeInk.Gray, bool locked = false)
    {
        var door = new OSHomeDoor
        {
            id = id, tile = new Vector2Int(x, y), targetRoom = target, arriveTile = new Vector2Int(ax, ay),
            label = label, labelKey = "os.home." + room.id + "." + id + ".label", ink = ink, locked = locked
        };
        room.doors.Add(door);
        return door;
    }

    static OSHomeThing Thing(OSHomeRoom room, string id, int x, int y, string glyph, string label,
        OSHomeInk ink = OSHomeInk.Gray, bool walk = false)
    {
        string key = "os.home." + room.id + "." + id;
        var thing = new OSHomeThing
        {
            id = room.id + "_" + id, tile = new Vector2Int(x, y), glyph = glyph, label = label,
            glyphKey = key + ".glyph", labelKey = key + ".label", ink = ink, blocksWalk = !walk
        };
        room.things.Add(thing);
        return thing;
    }

    static OSHomeLight Lamp(OSHomeRoom room, string id, OSHomeLightKind kind, float tx, float ty, float core, float band)
    {
        var light = new OSHomeLight
        {
            id = room.id + "_" + id, kind = kind, centre = new Vector2(tx * T + T * .5f, ty * T + T * .5f),
            core = core, band = band
        };
        room.lights.Add(light);
        return light;
    }

    static void Strip(OSHomeRoom room, string id, RectInt rect, float band)
    {
        room.lights.Add(new OSHomeLight { id = room.id + "_" + id, kind = OSHomeLightKind.Strip, strip = rect, band = band });
    }

    // Lightning round a curtain `width` tiles wide at (x, y): down both edges and under the hem.
    static void Leak(OSHomeRoom room, string id, int x, int y, int width)
    {
        int left = x * T, right = (x + width) * T, top = y * T, hem = (y + 1) * T;
        room.lights.Add(new OSHomeLight { id = room.id + "_" + id + "_l", kind = OSHomeLightKind.Lightning,
            strip = new RectInt(left, top, 2, 5 * T), band = 6f });
        room.lights.Add(new OSHomeLight { id = room.id + "_" + id + "_r", kind = OSHomeLightKind.Lightning,
            strip = new RectInt(right - 2, top, 2, 5 * T), band = 6f });
        room.lights.Add(new OSHomeLight { id = room.id + "_" + id + "_hem", kind = OSHomeLightKind.Lightning,
            strip = new RectInt(left, hem - 1, right - left, 2), band = 10f });
    }

    static void Water(OSHomeRoom room, params (int x, int y)[] tiles)
    {
        foreach (var t in tiles) room.water.Add(new Vector2Int(t.x, t.y));
    }

    static void Blood(OSHomeRoom room, params (int x, int y)[] tiles)
    {
        foreach (var t in tiles) room.blood.Add(new Vector2Int(t.x, t.y));
    }

    static void Foot(OSHomeRoom room, params (int x, int y)[] tiles)
    {
        foreach (var t in tiles) room.footprints.Add(new Vector2Int(t.x, t.y));
    }
}
