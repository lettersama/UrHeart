-- One rigid working hand travels on a tween path. No supporting hand.
-- Key fields: seconds,x,y,progress,contact,fx,angle.
-- Angle is optional; nil x/y means no working hand.
local function k(t,x,y,p,contact,fx,angle)
 return {t=t,x=x,y=y,p=p or 0,contact=contact or false,fx=fx or 0,angle=angle or 0}
end
return {
 {id='01_wipe',from='00_original',to='01_clean',kind='cloth',reveal='scatter',keys={
  k(0),k(.16),k(.24,140,177),k(.54,116,112),k(.80,86,62),
  k(.92,86,62,.05,true,1),k(1.20,108,59,.42,true,2),
  k(1.48,87,66,.72,true,3),k(1.76,111,70,1,true,4),
  k(1.96,114,93,1),k(2.24,132,132,1),k(2.44,144,180,1),
  k(2.54,nil,nil,1),k(2.82,nil,nil,1)}},
 {id='02_fill',from='01_clean',to='02_filled',kind='paste',reveal='fill',keys={
  k(0),k(.18),k(.28,143,172),k(.60,115,96),k(.86,89,44),
  k(1.10,89,44,.24,true,1),k(1.30,91,48,.25,true,2),
  k(1.52,84,73,.25),k(1.82,86,74,.49,true,3),k(2.00,89,76,.5,true,4),
  k(2.16,102,83,.5),k(2.42,104,83,.74,true,5),
  k(2.64,118,72,.75),k(2.96,119,74,1,true,6),
  k(3.22,129,98,1),k(3.60,151,174,1),k(3.72,nil,nil,1),k(4.04,nil,nil,1)}},
 {id='03_powder_left',from='02_filled',to='03_powder_left',kind='puff',reveal='top',keys={
  k(0),k(.18),k(.28,132,174),k(.58,102,100),k(.78,89,47),
  k(.98,89,44,.22,true,1),k(1.12,90,50,.32,false,2),k(1.28,87,46,.45,true,3),
  k(1.48,84,76,.55),k(1.64,84,73,.8,true,4),k(1.80,88,80,.85,false,5),
  k(1.98,84,74,1,true,6),k(2.22,107,100,1),k(2.58,139,177,1),k(2.72,nil,nil,1),k(3.02,nil,nil,1)}},
 {id='04_powder_right',from='03_powder_left',to='04_powder_both',kind='puff',reveal='scatter',keys={
  k(0),k(.18),k(.28,144,175),k(.58,121,104),k(.80,119,75),
  k(.98,119,73,.25,true,1),k(1.12,123,79,.35,false,2),k(1.28,119,73,.55,true,3),
  k(1.48,106,86,.65),k(1.66,103,83,1,true,4),k(1.84,107,90,1,false,5),
  k(2.18,128,115,1),k(2.48,146,177,1),k(2.62,nil,nil,1),k(2.92,nil,nil,1)}},
 {id='05_brows',from='04_powder_both',to='05_brows_uneven',kind='pencil',reveal='brows',keys={
  k(0),k(.20),k(.30,147,175),k(.64,102,95),k(.94,80,48),
  k(1.10,84,47,.22,true,1),k(1.34,89,48,.5,true,2),k(1.54,94,58,.5),
  k(1.82,111,50,.5),k(2.04,115,49,.75,true,3),k(2.26,120,50,1,true,4),
  k(2.42,125,63,1),k(2.62,131,99,1),k(2.98,150,178,1),k(3.10,nil,nil,1),k(3.52,nil,nil,1)}},
 {id='06_tilt',from='05_brows_uneven',to='06_brows_tilted',kind='none',reveal='pose',keys={
  k(0),k(.36),k(.48,nil,nil,0,false,0,-2),k(.62,nil,nil,0,false,0,-4),
  k(.78,nil,nil,0,false,0,-7),k(.92,nil,nil,0,false,0,-6),k(1.62,nil,nil,1,false,0,-6)}},
 {id='07_return',from='06_brows_tilted',to='07_neutral',kind='none',reveal='pose',keys={
  k(0,nil,nil,0,false,0,-6),k(.24,nil,nil,0,false,0,-6),
  k(.42,nil,nil,0,false,0,-4),k(.62,nil,nil,0,false,0,-2),k(.80,nil,nil,1),k(1.30,nil,nil,1)}},
 {id='08_cat',from='07_neutral',to='08_cat',kind='pencil',reveal='scatter',keys={
  k(0),k(.18),k(.28,149,175),k(.56,132,110),k(.82,115,71),
  k(1.08,115,69,.2,true,1),k(1.26,118,72,.42,true,2),k(1.44,122,69,.62,true,3),
  k(1.62,120,76,.84,true,4),k(1.84,114,78,1,true,5),k(2.02,122,86,1),
  k(2.24,131,113,1),k(2.56,149,177,1),k(2.68,nil,nil,1),k(3.08,nil,nil,1)}},
 {id='09_heart',from='07_neutral',to='09_heart',kind='pencil',reveal='scatter',keys={
  k(0),k(.18),k(.28,149,175),k(.56,132,110),k(.82,114,71),
  k(1.04,116,69,.22,true,1),k(1.24,118,71,.4,true,2),k(1.44,121,69,.62,true,3),
  k(1.64,121,73,.82,true,4),k(1.86,118,77,1,true,5),k(2.06,126,89,1),
  k(2.28,133,114,1),k(2.58,149,177,1),k(2.70,nil,nil,1),k(3.10,nil,nil,1)}}
}
