"""Author the picture scenario; Unity imports this manifest through its Editor API."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).parent
messages = {}
localization = []
labels = {}

def text(key, zh, en):
    index = 2700 + len(messages) * 10
    messages[key] = {"id": key, "index": index, "zh": zh}
    localization.append({"key": f"os.memory.{index}.text", "values": [zh, "", en, "", "", "", "", "", "", ""]})
    return index

def label(key, zh, en):
    full = "os.memory.pic." + key
    if key not in labels:
        labels[key] = full
        localization.append({"key": full, "values": [zh, "", en, "", "", "", "", "", "", ""]})
    return full

# Every accepted world/item action points to one of these normal OSMemoryData rows.
for row in [
 ("arrival","到家了。门把手冰凉，屋里一点声音也没有。","Home. The handle is cold. Not a sound inside."),
 ("enter","我推开门，抱着她走进走廊。这里怎么乱成这样？","I push the door open and carry her inside. What happened to the hallway?"),
 ("carry_first","先去<color=red>二楼工作室</color>，把机器人<color=red>放到工作台上</color>，再去其他房间看看吧。","I should take the robot to the <color=red>upstairs workshop</color> and <color=red>lay her on the workbench</color> before checking the other rooms."),
 ("stairs_up","我扶着栏杆上楼，尽量不去听身后的动静。","I climb the stairs, trying not to listen to the sounds behind me."),
 ("stairs_down","楼下安静得让我不舒服。","The quiet downstairs makes me uneasy."),
 ("hall","我回到走廊。","I return to the hallway."),
 ("kitchen","厨房的柜门敞着，椅子也倒了。","The kitchen cupboards are open. A chair is on its side."),
 ("living","窗户怎么被人砸破了，这世道真是不安宁...我该收拾一下玻璃，顺带把窗户<color=red>加固</color>一下，以防兽化者进来...","Who smashed the window? These are troubled times... I should clear up the glass and <color=red>reinforce</color> the window to keep the beastmen out..."),
 ("workshop","工作室里还是熟悉的机油味。","The workshop still smells of machine oil."),
 ("bedroom","我看了眼自己的房间。衣柜留着一道缝。","I look into my room. The wardrobe is slightly open."),
 ("daughter","我在女儿的门前停住。","I stop outside my daughter's door."),
 ("daughter_locked","被女儿上锁了，毕竟年轻人都有点隐私。","My daughter locked it. Young people need some privacy, after all."),
 ("door_ribbon","这个小结是她绑的。我没有动它。","She tied this little knot. I leave it alone."),
 ("outside","我又站到门前，听了听。","I stand by the front door again and listen."),
 ("door_after_knock","女儿回来了!? 我得下去开门……","My daughter's back!? I need to get the door..."),
 ("pot_up","我把花盆扶正。土还湿着，像是刚被踢倒不久。","I set the pot upright. The soil is still damp, as if it was kicked over recently."),
 ("pot_done","先让它好好待着吧。","I'll leave it upright."),
 ("drawer","抽屉被拉到了头。里面的东西全错了位置。","The drawer is pulled all the way out. Nothing is where I left it."),
 ("letters","信封还在，唯独下面的灰被蹭掉了一块。","The envelopes are here, but a patch of dust beneath them has been rubbed away."),
 ("scratches","这几道划痕……我记得原来没有。","These scratches... I don't remember them."),
 ("button","一颗旧纽扣。也可能是我自己掉的。","An old button. Maybe I dropped it myself."),
 ("picture","我扶正相框。它一直挂得好好的。","I straighten the picture. It was hanging perfectly before."),
 ("picture_done","照片里的人都看着镜头。","Everyone in the photograph is looking at the camera."),
 ("shoe","鞋尖朝着楼梯。我出门时是这样放的吗？","The shoe points towards the stairs. Did I leave it like that?"),
 ("robot_place","我终于把她放上工作台，胳膊已经酸得发抖。","I finally lay her on the workbench. My arms are trembling."),
 ("glass","砰！楼下传来玻璃碎裂的声音,听起来像是<color=red>客厅</color>传来的。我一下屏住了呼吸。","CRASH! Glass shatters downstairs. It sounds like it came from the <color=red>living room</color>. I hold my breath."),
 ("go_check","刚才的声音是楼下传来的。我得先去看看。","That noise came from downstairs. I need to check."),
 ("window_look","窗户破了。外面什么也看不清……先拿<color=red>楼上的木板和工具</color>堵住这里。","The window is broken. I can't see anything outside... I'll get the <color=red>planks and tools from upstairs</color> to board it up."),
 ("need_wood","还缺<color=red>木板和钉子</color>。去工作室一并拿上，再从工具箱里拿把<color=red>锤子</color>。","I still need <color=red>planks and nails</color>. I can pick them up together in the workshop, then get a <color=red>hammer</color> from the toolbox."),
 ("take_wood","这几块旧木板还能用。再带上一小盒钉子。","These old planks will do. I take a small box of nails as well."),
 ("already_taken","已经拿好了。","I've already taken it."),
 ("toolbox","我打开工具箱，把能用的东西拣出来。","I open the toolbox and sort through what's usable."),
 ("take_hammer","锤子还在。","The hammer is still here."),
 ("take_nails","还有一小盒钉子。应该够了。","There's a small box of nails. It should be enough."),
 ("board","我把木板压在窗框上。风还在从缝里钻进来。","I hold a plank against the frame. Wind still slips through the gaps."),
 ("board_last","最后一块。接下来把它们钉牢。","The last plank. Now to nail them firmly in place."),
 ("need_tools","木板得用锤子和钉子固定，不能就这样松着。","I need the hammer and nails. I can't leave these planks loose."),
 ("nail_start","先放钉子,每块木板的两端都要固定...","Nails first. Both ends of each plank need securing..."),
 ("nail_done","这颗钉牢了。","That nail is secure."),
 ("window_done","都钉牢了。至少暂时不用再盯着那个黑洞。","All secure. At least I can stop staring into that dark hole for now."),
 ("window_wait","先把三块木板放稳，再钉钉子。","I should position all three planks before hammering."),
 ("clean_first","窗户堵好了。先把楼下收拾完，再安心修她。","The window is covered. I'll finish tidying downstairs before repairing her."),
 ("chair","我把椅子扶起来，放回桌边。","I set the chair upright and put it back at the table."),
 ("chair_done","椅子已经放好了。","The chair is back in place."),
 ("sink","碗碟被胡乱丢在水槽里。","The dishes have been dumped in the sink."),
 ("take_cloth","拿上这块抹布。","I'll take this cloth."),
 ("dishes_need","找块抹布，把这些碗碟收拾一下。","I need a cloth to clean up these dishes."),
 ("dishes","擦干净，再把碗碟放好。做这些小事让我稍微平静了一点。","I wipe the dishes and put them away. Small tasks calm me a little."),
 ("dishes_done","水槽已经收拾干净了。","The sink is tidy."),
 ("tap","水管里响了一下，随后又安静了。","Something knocks inside the pipe. Then silence."),
 ("take_broom","扫帚靠在墙边，可以用来收拾地上的垃圾。","A broom leans against the wall. It could be used to sweep up the rubbish on the floor."),
 ("cupboard","罐头没有少。翻东西的人是在找什么？","The tins are all here. What was someone looking for?"),
 ("cup","杯底还留着一点干掉的茶。","A little dried tea remains at the bottom of the cup."),
 ("table","桌上的东西被挪过。相框扣在书旁边。","Things on the table have been moved. The photo lies face down beside a book."),
 ("books","桌上摞着几本翻旧了的书。","A few well-worn books are stacked on the table."),
 ("books_done","书已经收好了。","The books are tidy."),
 ("photo","相框反扣在桌上，只露出背面的支架。","The photo frame lies face down on the table. Only the stand on its back is visible."),
 ("cushion","我把靠垫摆回原位。","I put the cushion back."),
 ("cushion_done","这样看起来像家一点了。","It looks a little more like home."),
 ("glass_need","得用<color=red>扫帚</color>清理玻璃，不能直接拿手碰。","I need the <color=red>broom</color> to clear up the glass. I shouldn't touch it with my bare hands."),
 ("glass_sweep_wait","先把窗户钉牢，免得再掉碎片。","I'll secure the window first, in case more glass falls."),
 ("glass_sweep","我扫净地上的玻璃，反复确认没有漏下碎片。","I sweep up the glass and check that I haven't missed any shards."),
 ("glass_clean","地上已经没有玻璃了。","The floor is clear of glass."),
 ("blanket","被子还是出门前的样子。我不想在这里多待。","The blanket is just as I left it. I don't want to linger."),
 ("wardrobe","里面只有衣服。我慢慢把手松开。","Only clothes inside. I slowly unclench my hand."),
 ("take_tape","绝缘胶带。我之前修灯时忘在这里的。","Electrical tape. I left it here when I fixed the lamp."),
 ("take_fuse","找到一枚合适的保险丝。","I've found a suitable fuse."),
 ("repair","打开接口盖。先断电，再把脱开的接头一一接回去。","I open the connector cover. Power off first, then reconnect the loose plugs."),
 ("power_off","电源断开了。接口的形状各不相同，照着形状接就行。","Power is off. Each connector has a different shape. I can match them."),
 ("power_required","先断开电源再动这些线。","I should disconnect the power before touching the wires."),
 ("wire_select","我捏住这个接头，找它对应的接口。","I take hold of the plug and look for the matching socket."),
 ("wire_first","先选一个松脱的接头。","I need to choose a loose plug first."),
 ("wire_wrong","形状对不上，不能硬塞。","The shapes don't match. I shouldn't force it."),
 ("wire_connect","接上了。","Connected."),
 ("wire_done","这个接头已经接好了。","That plug is already connected."),
 ("need_fuse","保险丝烧断了，工作室的工具箱里应该有备用的。","The fuse has blown. There should be a spare in the workshop toolbox."),
 ("fuse_fix","我把烧断的保险丝换掉。","I replace the blown fuse."),
 ("fuse_done","新的保险丝已经装好了。","The new fuse is fitted."),
 ("need_tape","还得把裸露的地方包好。我记得胶带放在卧室床头。","The exposed section needs insulating. I remember leaving tape by my bed."),
 ("tape_fix","我把露出的线包紧，轻轻拉了一下，没松。","I wrap the exposed wire and tug gently. It holds."),
 ("need_wires","还有松脱的接头，先把它们接完。","Some plugs are still loose. I need to finish connecting them."),
 ("need_finish","保险丝和绝缘都处理好了，才能通电测试。","I need to finish the fuse and insulation before testing the power."),
 ("robot_done","指示灯终于亮了。我松了一口气，手心全是汗。","The indicator finally lights up. I breathe out. My palms are soaked."),
 ("knock","<color=red>女儿回来了!?</color>","<color=red>My daughter's back!?</color>"),
 ("robot_ready","她暂时稳定下来了。门外的动静更让我在意。","She's stable for now. I'm more worried about the noise at the door."),
 ("item_robot","得先把她放到楼上的工作台。","I need to get her onto the workbench upstairs."),
 ("item_wood","三块旧木板，足够遮住破掉的窗户。","Three old planks. Enough to cover the broken window."),
 ("item_hammer","一把旧锤子，握柄很熟悉。","An old hammer with a familiar grip."),
 ("item_nails","一小盒钉子，别弄撒了。","A small box of nails. I mustn't spill them."),
 ("item_broom","用它扫掉碎玻璃。","I'll use it to sweep up the glass."),
 ("item_cloth","还能用的旧抹布。","An old cloth that's still usable."),
 ("item_tape","绝缘胶带，修机器人会用得上。","Electrical tape. It'll help with the robot."),
 ("item_fuse","型号合适的备用保险丝。","A spare fuse of the right type."),
 ("check_living_first","我应该先去检查一下<color=red>客厅</color>的声响...","I should check the noise in the <color=red>living room</color> first..."),
 ("parts_inspect","盒子里装着一些拆下来的旧零件，暂时用不上。","The box holds some old salvaged parts. I have no use for them right now."),
 ("planks_inspect","墙边靠着几块旧木板，边角已经磨损了。","Some old planks lean against the wall. Their edges are worn."),
 ("toolbox_inspect","这只工具箱用了很多年，箱盖上满是划痕。","This toolbox has seen years of use. Its lid is covered in scratches."),
 ("bedroom_photo_inspect","女儿还那么小的时候，总爱贴着我。","When she was so little, my daughter was always clinging to me."),
 ("board_second","第二块也放稳了，还差<color=red>一块木板</color>。","The second plank is in place. Just <color=red>one more plank</color> to go."),
 ("nail_guide","三块木板已经放稳。每块左右各一颗，共<color=red>六颗钉子</color>，放好后再逐颗敲牢。","All three planks are in place. One nail at each end makes <color=red>six nails</color>. Place each nail, then hammer it in."),
 ("nail_place","钉子放好了，再用<color=red>锤子</color>把它敲牢。","The nail is in position. Now hammer it in with the <color=red>hammer</color>."),
 ("nail_all_done","六颗钉子都钉牢了，窗户已经加固好了。","All six nails are secure. The window is reinforced."),
 ("living_after_window","窗户已经加固好了。还得用<color=red>扫帚</color>把地上的碎玻璃清理干净。","The window is reinforced. I still need to sweep up the broken glass with the <color=red>broom</color>."),
 ("living_after_glass","碎玻璃也清理干净了，可以<color=red>上楼修机器人</color>了。","The broken glass is cleared away. I can <color=red>go upstairs and repair the robot</color> now."),
 ("stairs_after_window","我扶着栏杆上楼。窗户已经钉牢，楼下也安静下来了。","I climb the stairs, holding the railing. The window is secure, and downstairs is quiet again."),
 ("need_hammer","钉子已经放好，还得拿<color=red>锤子</color>来敲牢。","The nail is in position. I need a <color=red>hammer</color> to drive it in."),
 ("need_nails","去<color=red>工作室</color>拿木板时，把钉子一起带上。","I should pick up the nails with the planks in the <color=red>workshop</color>."),
 ("go_to_front_door","我应该去门口看看！说不定是<color=red>女儿</color>回来了。","I should check the front door! Maybe my <color=red>daughter</color> is back."),
 ("hall_picture_done","相框已经摆正了。画里的小屋静静立在树边。","The frame is straight now. The little house in the picture stands quietly beside the trees."),
 ("robot_window_first","东西都拿齐了。先去<color=red>客厅加固窗户</color>，再回来修她。","I have everything I need. I'll <color=red>reinforce the living-room window</color> first, then come back to repair her."),
 ("robot_need_hammer","木板和钉子已经拿好了。还得从<color=red>工具箱</color>里拿把<color=red>锤子</color>。","I've collected the planks and nails. I still need a <color=red>hammer</color> from the <color=red>toolbox</color>."),
]: text(*row)

def words(value):
    return value.split() if isinstance(value, str) else list(value or [])

def condition(need="", no="", items=""):
    return {"allFlags": words(need), "noFlags": words(no), "items": words(items)}

def outcome(message, need="", no="", items="", flags="", remove_flags="", add="", remove="", target="", sound="", **extra):
    return dict(when=condition(need,no,items),memoryIndex=messages[message]["index"],addFlags=words(flags),
                removeFlags=words(remove_flags),addItems=words(add),removeItems=words(remove),
                targetScene=target,soundCue=sound,**extra)

scenes = []
def scene(id,zh,en):
    s={"sceneId":id,"titleKey":label("scene."+id,zh,en),"hotspots":[],"layerConditions":{}}
    scenes.append(s)
    return s

def hot(s,id,zh,en,art,outcomes,rect=None,need="",no="",glitch=True,kind=0,index=0,arrow=None):
    h={"hotspotId":id,"labelKey":label(id,zh,en),"art":words(art),"outcomes":outcomes,
       "visibleWhen":condition(need,no),"glitch":glitch,"kind":kind,"socketIndex":index}
    if rect: h["rect"]=rect
    if arrow: h["arrow"]=arrow
    s["hotspots"].append(h)
    return h

def state_layer(s,name,need="",no=""):
    s["layerConditions"][name]=condition(need,no)

def living_outcomes():
    return [outcome("living_after_glass",need="window_fixed glass_swept",target="living"),
            outcome("living_after_window",need="window_fixed",target="living"),
            outcome("living",target="living")]

def board_outcomes():
    # Inventory possession is enough; selecting an item is never required.
    return [outcome("board_last",need="board_1",items="wood",flags="board_2 boards_placed",remove="wood"),
            outcome("board_second",need="board_0",items="wood",flags="board_1"),
            outcome("board",items="wood",flags="board_0")]

def back(s,to,message="hall"):
    results=living_outcomes() if to=="living" else [outcome(message,target=to)]
    return hot(s,"back."+s["sceneId"],"返回","Back","",results,glitch=False)

# Scene definitions follow.
s=scene("entrance","家门前","Front door")
hot(s,"enter","进门","Enter","handle",[outcome("door_after_knock",need="knock_heard"),outcome("enter",target="hall",sound="door")])

s=scene("hall","楼下走廊","Downstairs hall")
# The pot is no longer interactive; retain text keys and old visual state compatibility.
label("pot","扶起花盆","Set pot upright")
state_layer(s,"pot_down",no="pot_up");state_layer(s,"pot_up",need="pot_up")
hot(s,"hall_drawer","查看抽屉","Examine drawer","drawer",[outcome("drawer",target="drawer")])
hot(s,"hall_picture","扶正相框","Straighten picture","picture picture_straight",
    [outcome("hall_picture_done",need="picture_fixed",tooltipLabelKey=label("hall_picture_inspect","查看相框","Look at picture")),outcome("picture",flags="picture_fixed")])
state_layer(s,"picture",no="picture_fixed");state_layer(s,"picture_straight",need="picture_fixed")
hot(s,"hall_letters","查看信件","Examine letters","letters",[outcome("letters")])
hot(s,"shoe","查看鞋子","Examine shoe","shoe",[outcome("shoe")])
for id,zh,en,target,msg in [
 ("to_kitchen","厨房","Kitchen","kitchen","kitchen"),
 ("to_living","客厅","Living room","living","living"),
]:
 results=[outcome("carry_first",no="robot_placed",speakWhenNavigationBlocked=True)]
 if target=="kitchen":
  results.append(outcome("check_living_first",need="robot_placed",no="window_inspected",speakWhenNavigationBlocked=True))
 results.extend(living_outcomes() if target=="living" else [outcome(msg,target=target)])
 hot(s,id,zh,en,"",results,glitch=False)
hot(s,"upstairs","上楼","Go upstairs","",[outcome("stairs_after_window",need="window_fixed",target="upstairs",sound="stairs_up"),outcome("stairs_up",target="upstairs",sound="stairs_up")],glitch=False)
# Once inside, the outside view is no longer reachable from the hall.
label("back.hall","返回","Back")

s=scene("upstairs","楼上走廊","Upstairs landing")
hot(s,"to_workshop","工作室","Workshop","workshop_door",[outcome("workshop",target="workshop")],rect=[56,88,65,109])
hot(s,"to_bedroom","我的房间","My room","bedroom_door",[outcome("bedroom",target="bedroom")],rect=[333,98,67,110])
hot(s,"to_daughter","女儿的房间","Daughter's room","daughter_door",[outcome("daughter",target="daughter")])
hot(s,"landing_photo","看照片","Look at photo","portrait",[outcome("picture_done")])
back(s,"hall","stairs_down")

s=scene("daughter","女儿房门","Daughter's door")
hot(s,"daughter_handle","试试门把手","Try the handle","handle",[outcome("daughter_locked")])
hot(s,"daughter_note","看看门上","Look at the door","door_note",[outcome("door_ribbon")])
back(s,"upstairs")

s=scene("workshop","工作室","Workshop")
clean_flags="glass_swept"
results=[outcome("robot_ready",need="robot_repaired"),
         outcome("robot_place",no="robot_placed",flags="robot_placed",remove="robot",glassStartle=True),
         outcome("go_check",no="window_inspected"),
         outcome("robot_window_first",need="wood_taken nails_taken hammer_taken",no="window_fixed"),
         outcome("robot_need_hammer",need="wood_taken nails_taken",no="window_fixed"),
         outcome("need_nails",need="hammer_taken",no="window_fixed"),
         outcome("need_wood",no="window_fixed")]
results.extend(outcome("living_after_window",no=f) for f in clean_flags.split())
results.append(outcome("repair",target="wiring"))
hot(s,"robot","工作台","Workbench","bench robot",results,rect=[195,176,162,80])
state_layer(s,"robot",need="robot_placed")
hot(s,"planks","木板和钉子","Planks and nails","planks",
    [outcome("planks_inspect",no="window_inspected"),
     outcome("take_wood",need="window_inspected",no="wood_taken",flags="wood_taken nails_taken",add="wood nails"),outcome("already_taken")],no="wood_taken")
state_layer(s,"planks",no="wood_taken")
hot(s,"toolbox","工具箱","Toolbox","toolbox",
    [outcome("toolbox_inspect",no="window_inspected"),outcome("toolbox",need="window_inspected",target="toolbox")])
hot(s,"parts","查看零件盒","Inspect parts box","parts",[outcome("parts_inspect")])
back(s,"upstairs")

s=scene("toolbox","工具箱","Toolbox")
# Keep the old localized label for saved/history data; nails no longer need a separate click.
label("take_nails","拿钉子","Take nails")
for item,zh,en,msg in [("hammer","拿锤子","Take hammer","take_hammer"),("fuse","拿保险丝","Take fuse","take_fuse")]:
 # Keep the inspection prerequisite even when resuming an older save inside the toolbox.
 hot(s,"take_"+item,zh,en,item,[outcome(msg,need="window_inspected",flags=item+"_taken",add=item)],need="window_inspected",no=item+"_taken")
 state_layer(s,item,no=item+"_taken")
state_layer(s,"nails",no="nails_taken")
back(s,"workshop","workshop")

s=scene("kitchen","厨房","Kitchen")
# The chair is artwork only; keep existing text keys and old visual state compatibility.
label("chair","扶起椅子","Set chair upright")
state_layer(s,"chair_down",no="chair_up");state_layer(s,"chair_up",need="chair_up")
hot(s,"to_sink","收拾水槽","Tidy the sink","sink",[outcome("sink",target="sink")],rect=[184,133,89,37])
hot(s,"take_broom","拿扫帚","Take broom","broom",[outcome("take_broom",flags="broom_taken",add="broom")],no="broom_taken")
state_layer(s,"broom",no="broom_taken")
hot(s,"cupboard","检查柜子","Check cupboard","cupboard",[outcome("cupboard")])
hot(s,"cup","看看杯子","Look at cup","cup",[outcome("cup")])
back(s,"hall")

s=scene("sink","水槽","Sink")
hot(s,"take_cloth","拿抹布","Take cloth","cloth",[outcome("take_cloth",flags="cloth_taken",add="cloth")],no="cloth_taken")
state_layer(s,"cloth",no="cloth_taken")
hot(s,"dishes","收拾碗碟","Clean the dishes","dishes dishes_clean",
    [outcome("dishes_done",need="dishes_clean"),outcome("dishes",items="cloth",flags="dishes_clean"),outcome("dishes_need")])
state_layer(s,"dishes",no="dishes_clean");state_layer(s,"dishes_clean",need="dishes_clean")
hot(s,"tap","摸摸水龙头","Check the tap","tap",[outcome("tap")])
back(s,"kitchen","kitchen")

s=scene("living","客厅","Living room")
hot(s,"window","检查窗户","Inspect window","window_broken window_boards_1 window_boards_2 window_boarded",
    [outcome("window_done",need="window_fixed",target="window"),outcome("nail_guide",need="boards_placed",target="window")]
    +[outcome("window_wait",items="wood",flags="window_inspected",target="window"),
      outcome("window_look",flags="window_inspected",target="window")],
    rect=[282,48,111,131])
state_layer(s,"window_broken",need="glass_broken",no="board_0")
state_layer(s,"window_boards_1",need="board_0",no="board_1")
state_layer(s,"window_boards_2",need="board_1",no="boards_placed")
state_layer(s,"window_boarded",need="boards_placed")
hot(s,"cushion","摆好靠垫","Replace cushion","cushion_down cushion_up",
    [outcome("cushion_done",need="cushion_up"),outcome("cushion",flags="cushion_up")])
state_layer(s,"cushion_down",no="cushion_up");state_layer(s,"cushion_up",need="cushion_up")
hot(s,"to_table","查看茶几","Look at coffee table","table",[outcome("table",target="table")],rect=[182,212,118,50])
hot(s,"sweep_glass","扫掉玻璃","Sweep up glass","glass",
    [outcome("glass_sweep_wait",no="window_fixed"),outcome("glass_sweep",items="broom",flags="glass_swept"),outcome("glass_need")],
    need="glass_broken",no="glass_swept")
state_layer(s,"glass",need="glass_broken",no="glass_swept")
back(s,"hall")

s=scene("table","茶几","Coffee table")
hot(s,"books","查看书本","Look at books","books",
    [outcome("books")])
state_layer(s,"books");state_layer(s,"books_stacked",need="unused_layer")
hot(s,"family_photo","查看相框","Look at photo frame","photo",[outcome("photo")])
state_layer(s,"photo");state_layer(s,"photo_up",need="unused_layer")
hot(s,"cup","看看杯子","Look at cup","cup",[outcome("cup")])
back(s,"living","living")

s=scene("drawer","走廊抽屉","Hall drawer")
hot(s,"drawer_letters","翻看信件","Examine letters","letters",[outcome("letters")])
# Keep the existing text key and Memory index; scratches are artwork only.
label("scratches","看看划痕","Look at scratches")
hot(s,"button","看看纽扣","Look at button","button",[outcome("button")])
back(s,"hall")

s=scene("bedroom","我的房间","My room")
state_layer(s,"tape",need="unused_layer")
hot(s,"bedroom_photo","看床头照片","Look at bedside photo","photo",[outcome("bedroom_photo_inspect")])
hot(s,"wardrobe","看看衣柜","Look in wardrobe","wardrobe",[outcome("wardrobe")])
hot(s,"blanket","看看床铺","Look at the bed","blanket",[outcome("blanket")])
back(s,"upstairs")

s=scene("window","窗户特写","Window")
hot(s,"place_board","放置木板","Place a plank","frame_target",
    board_outcomes()+[outcome("need_wood")],
    rect=[77,138,269,117],no="boards_placed")
for i in range(3): state_layer(s,"board_"+str(i),need="board_"+str(i))
hammer_label=label("hammer_nail","锤钉子","Hammer nail")
for i in range(6):
 flag=f"nail_{i}"
 x=78 if i%2==0 else 337
 y=146+(i//2)*39+13+(i%2)*5
 other_done=" ".join(f"nail_{j}_done" for j in range(6) if j!=i)
 hot(s,flag,"放置钉子","Place nail","nail_marker_"+str(i)+" "+flag+" nail_done_"+str(i),
     [outcome("nail_all_done",need=flag+"_hit "+other_done,items="hammer",flags=flag+"_done",sound="hammer",tooltipLabelKey=hammer_label),
      outcome("nail_done",need=flag+"_hit",items="hammer",flags=flag+"_done",sound="hammer",tooltipLabelKey=hammer_label),
      outcome("need_hammer",need=flag+"_hit",tooltipLabelKey=hammer_label),
      outcome("nail_place",need="nails_started",items="nails",flags=flag+"_hit"),
      outcome("nail_start",items="nails",flags=flag+"_hit nails_started"),outcome("need_nails")],
     rect=[x-10,y-12,24,26],need="boards_placed",no=flag+"_done")
 state_layer(s,"nail_marker_"+str(i),need="boards_placed",no=flag+"_hit "+flag+"_done")
 state_layer(s,flag,need="boards_placed "+flag+"_hit",no=flag+"_done")
 state_layer(s,"nail_done_"+str(i),need=flag+"_done")
back(s,"living","living")

s=scene("wiring","机器人接口","Robot connectors")
hot(s,"power","断开电源","Disconnect power","power power_off",
    [outcome("robot_ready",need="robot_repaired",tooltipLabelKey="os.memory.pic.test_power"),
     outcome("power_off",flags="power_off")],no="power_off")
state_layer(s,"power",no="power_off");state_layer(s,"power_off",need="power_off")
mapping=[2,0,3,1]
for i in range(4):
 hot(s,f"plug_{i}","选择接头","Choose plug",f"plug_{i}",[],kind=1,index=i,no=f"wire_{i}")
 hot(s,f"socket_{i}","接入接口","Connect to socket",f"socket_{i}",[],kind=2,index=mapping[i],no=f"wire_{mapping[i]}")
 state_layer(s,f"wire_{i}",need="unused_layer")
hot(s,"replace_fuse","更换保险丝","Replace fuse","fuse fuse_fixed",
    [outcome("power_required",no="power_off"),outcome("fuse_done",need="fuse_fixed"),
     outcome("fuse_fix",items="fuse",flags="fuse_fixed",remove="fuse"),outcome("need_fuse")])
state_layer(s,"fuse",no="fuse_fixed");state_layer(s,"fuse_fixed",need="fuse_fixed")
hot(s,"test_power","通电测试","Test power","power_off",
    [outcome("robot_ready",need="robot_repaired"),outcome("need_wires",no="wires_connected"),
     outcome("need_fuse",no="fuse_fixed"),
     outcome("robot_done",finishRepair=True,energyCost=5)],
    rect=[278,241,39,31],need="power_off")
back(s,"workshop","workshop")["outcomes"].insert(0,
    outcome("go_to_front_door",need="robot_repaired",target="workshop"))

# Foley belongs to successful material actions. Inspection, blocked conditions,
# and repeated completion messages remain quiet. Room movement is scoped by
# hotspot so returning from an object close-up does not produce footsteps.
action_sounds={
 "drawer":"drawer", "picture":"wood",
 "robot_place":"robot_place",
 "take_wood":"wood", "toolbox":"toolbox",
 "take_hammer":"metal_pickup", "take_nails":"metal_pickup", "take_fuse":"metal_pickup",
 "take_broom":"pickup", "take_cloth":"cloth", "dishes":"dishes", "cushion":"cloth",
 "glass_sweep":"sweep", "board_last":"wood", "board_second":"wood", "board":"wood",
 "nail_place":"nail_place", "nail_start":"nail_place",
 "power_off":"power_off", "fuse_fix":"fuse", "robot_done":"power_on",
}
action_sounds_by_index={messages[key]["index"]:cue for key,cue in action_sounds.items()}
movement_sounds={
 ("hall","to_kitchen"):"walk", ("hall","to_living"):"walk",
 ("upstairs","to_workshop"):"door", ("upstairs","to_bedroom"):"door",
 ("upstairs","to_daughter"):"walk", ("upstairs","back.upstairs"):"stairs_down",
 ("daughter","back.daughter"):"walk", ("workshop","back.workshop"):"walk",
 ("kitchen","back.kitchen"):"walk", ("living","back.living"):"walk",
 ("bedroom","back.bedroom"):"walk",
}
for s in scenes:
 for h in s["hotspots"]:
  movement=movement_sounds.get((s["sceneId"],h["hotspotId"]))
  for result in h["outcomes"]:
   cue=action_sounds_by_index.get(result["memoryIndex"])
   if movement and result.get("targetScene"):
    cue=movement
   if cue:
    result["soundCue"]=cue

# Fixed screen directions reuse existing interactions and their conditional outcomes.
# Close-ups remain object interactions; down returns from a close-up or room.
navigation_routes={
 "entrance":{"up":"enter"},
 "hall":{"up":"upstairs","left":"to_kitchen","right":"to_living"},
 "upstairs":{"up":"to_daughter","left":"to_workshop","right":"to_bedroom"},
 "kitchen":{"right":"back.kitchen"},
 "living":{"left":"back.living"},
 "workshop":{"right":"back.workshop"},
 "bedroom":{"left":"back.bedroom"},
}
for s in scenes:
 s["navigation"]={direction:"" for direction in ("up","down","left","right")}
 s["navigation"].update(navigation_routes.get(s["sceneId"],{}))
 return_id="back."+s["sceneId"]
 if any(h["hotspotId"]==return_id for h in s["hotspots"]): s["navigation"]["down"]=return_id

# Production art has different object positions. Keep gameplay IDs/results stable
# and keep all native-pixel layout adjustments in one reviewable authoring file.
layout_path=OUT/"paint-layout.json"
if layout_path.exists():
 layout=json.loads(layout_path.read_text(encoding="utf-8"))
 for s in scenes:
  edits=layout.get(s["sceneId"],{})
  for h in s["hotspots"]:
   if h["art"]:
    h.pop("rect",None)  # Default to the current exported silhouette bounds.
   if h["hotspotId"] in edits.get("hotspots",{}):
    h.update(edits["hotspots"][h["hotspotId"]])
  for name,flags in edits.get("layers",{}).items():
   s["layerConditions"][name]=condition(**flags)

items=[]
for id,zh,en in [("robot","机器人","Robot"),("wood","木板","Planks"),("hammer","锤子","Hammer"),("nails","钉子","Nails"),
                 ("broom","扫帚","Broom"),("cloth","抹布","Cloth"),("tape","绝缘胶带","Electrical tape"),("fuse","保险丝","Fuse")]:
 items.append({"itemId":id,"labelKey":label("item."+id,zh,en),"memoryIndex":messages["item_"+id]["index"],"iconName":id})

data={"scenarioId":"chapter01_home","entryMemoryIndex":2680,"firstSceneId":"entrance",
      "firstMemoryIndex":messages["arrival"]["index"],"initialItems":["robot"],
      "scenes":scenes,"items":items,"messages":list(messages.values()),
      "memoryIndices":[m["index"] for m in messages.values()],
      "localization":localization,"sounds":[{"cueId":x} for x in [
          "door","stairs_up","stairs_down","walk","door_locked","drawer","paper","wood","cloth",
          "pickup","metal_pickup","toolbox","robot_place","nail_place","hammer","glass","sweep",
          "dishes","power_off","connect","fuse","power_on","knock"]],
      "wrongSocketMemoryIndex":messages["wire_wrong"]["index"],"selectWireMemoryIndex":messages["wire_select"]["index"],
      "connectedWireMemoryIndex":messages["wire_connect"]["index"],"alreadyConnectedMemoryIndex":messages["wire_done"]["index"],
      "powerRequiredMemoryIndex":messages["power_required"]["index"],"cableRequiredMemoryIndex":messages["wire_first"]["index"],
      "glassMemoryIndex":messages["glass"]["index"],"knockMemoryIndex":messages["knock"]["index"]}
translation_path=OUT/"translations.json"
if translation_path.exists() and "--source-only" not in sys.argv:
 translations=json.loads(translation_path.read_text(encoding="utf-8-sig"))
 for entry in localization:
  if entry["key"] in translations:
   values=translations[entry["key"]]
   assert len(values)==10 and values[0]==entry["values"][0] and values[2]==entry["values"][2],entry["key"]
   entry["values"]=values
(OUT/"content.json").write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding="utf-8")
print(f"{len(scenes)} scenes, {sum(len(s['hotspots']) for s in scenes)} hotspots, {len(messages)} memory nodes, {len(localization)} localization keys")
