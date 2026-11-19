from pathlib import Path
import cairosvg, zipfile, json, math
from PIL import Image,ImageDraw,ImageFont
OUT=Path(__file__).resolve().parent.parent/'Resources'; OUT.mkdir(exist_ok=True)
N='#102B50'; B='#A86735'; G='#AAB3C0'; LG='#E9EEF4'
def line(x1,y1,x2,y2,c=N,w=4,extra=''): return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{c}" stroke-width="{w}" {extra}/>'
def path(d,c=N,w=4,fill='none',extra=''):return f'<path d="{d}" stroke="{c}" stroke-width="{w}" fill="{fill}" {extra}/>'
def rect(x,y,w,h,c=N,sw=4,fill='none',rx=2,extra=''):return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" stroke="{c}" stroke-width="{sw}" fill="{fill}" {extra}/>'
def circ(x,y,r,c=N,sw=3,fill='white'):return f'<circle cx="{x}" cy="{y}" r="{r}" stroke="{c}" stroke-width="{sw}" fill="{fill}"/>'
def txt(x,y,s,size=11,c=N,bold=True):return f'<text x="{x}" y="{y}" fill="{c}" font-family="DejaVu Sans,Arial" font-weight="{700 if bold else 400}" font-size="{size}" text-anchor="middle">{s}</text>'
def arrow(x1,y1,x2,y2,c=B,w=3):
 a=math.atan2(y2-y1,x2-x1); q=6; x=x2-q*math.cos(a);y=y2-q*math.sin(a);px=3.5*math.sin(a);py=-3.5*math.cos(a)
 return line(x1,y1,x2,y2,c,w)+f'<path d="M {x2} {y2} L {x+px} {y+py} L {x-px} {y-py} Z" fill="{c}"/>'
def badge(symbol):
 s=circ(48,46,11,B,2,B)
 if symbol=='plus': s+=line(42,46,54,46,'white',3)+line(48,40,48,52,'white',3)
 if symbol=='refresh':s+=path('M 42 43 A 7 7 0 0 1 53 42 M 54 49 A 7 7 0 0 1 43 50','white',2.5)+path('M 53 37 L 55 44 L 48 43','white',1.5,'white')+path('M 43 55 L 41 48 L 48 49','white',1.5,'white')
 if symbol=='stack':s+=path('M 41 43 L 48 39 L 55 43 L 48 47 Z','white',1.7)+path('M 41 47 L 48 51 L 55 47 M 41 51 L 48 55 L 55 51','white',1.7)
 return s
icons={}
# SHEET 1
icons['AlignTitle']=rect(9,10,46,35)+line(32,12,32,43,B,2,'stroke-dasharray="4 4"')+line(12,27,52,27,B,2,'stroke-dasharray="4 4"')+line(15,50,49,50,N,4)+line(12,56,52,56,B,3)
icons['AutoTag']=path('M 9 24 L 27 14 L 46 24 L 28 35 Z')+path('M 9 24 V 45 L 28 55 L 28 35 M 46 24 V 45 L 28 55')+path('M 28 35 L 44 21 L 50 14',B,3)+circ(28,35,3.5,B,1,B)+path('M 42 6 H 57 V 22 H 42 L 38 17 Z',B,2.5,'white')+line(45,12,54,12,N,2)+line(45,17,51,17,N,2)
icons['CreateSheet']=path('M 13 7 H 40 L 52 19 V 54 H 13 Z')+path('M 40 7 V 19 H 52')+rect(18,25,28,12,G,2,LG,1)+rect(18,40,28,10,N,2,'none',0)+line(30,40,30,50,N,2)+badge('plus')
icons['ViewBrowser']=rect(8,7,48,50,N,4,'none',4)+line(8,17,56,17,N,3)+circ(14,12,1,B,1,B)+circ(20,12,1,B,1,B)+rect(16,24,12,8,B,2,LG,1)+line(22,33,22,48,N,2)+line(22,39,31,39,N,2)+line(22,48,31,48,N,2)+rect(34,34,16,10,N,2,LG,1)+rect(34,45,16,8,N,2,LG,1)
icons['WitnessLine']=path('M 11 52 V 42 H 33 V 30 H 53 V 52 Z',N,4,LG)+line(11,13,11,35,B,2.5)+line(53,13,53,25,B,2.5)+arrow(30,16,15,16,B,2.8)+arrow(34,16,49,16,B,2.8)
# SHEET 2
icons['CropFit']=path('M 7 24 V 8 H 23 M 41 8 H 57 V 24 M 7 40 V 56 H 23 M 41 56 H 57 V 40')+rect(25,25,14,14,N,3,LG,1)+arrow(16,16,25,25)+arrow(48,16,39,25)+arrow(16,48,25,39)+arrow(48,48,39,39)
icons['CropOffset']=rect(10,10,44,44,N,2,'none',3,'stroke-dasharray="5 4"')+rect(23,23,18,18,N,3,LG,1)+arrow(32,14,32,22)+arrow(32,50,32,42)+arrow(14,32,22,32)+arrow(50,32,42,32)
icons['CropRegion']=rect(12,12,40,40,N,4,LG,1)+''.join(rect(x-4,y-4,8,8,N,2,'#C58B59',1) for x in (12,52) for y in (12,52))+path('M 37 34 L 38 55 L 43 49 L 49 55 L 52 52 L 46 46 L 54 43 Z',N,2,'white')
icons['DimensionGrid']=line(15,17,15,49,N,2,'stroke-dasharray="5 3"')+line(32,17,32,49,N,2,'stroke-dasharray="5 3"')+line(49,17,49,49,N,2,'stroke-dasharray="5 3"')+''.join(circ(x,12,8,N,2,'white')+txt(x,16,t,10) for x,t in ((15,'A'),(32,'B'),(49,'C')))+line(15,52,49,52,B,3)+line(15,47,15,57,B,2)+line(32,47,32,57,B,2)+line(49,47,49,57,B,2)+arrow(26,52,18,52,B,2)+arrow(38,52,46,52,B,2)
icons['DimensionLevel']= ''.join(path(f'M 12 {y} L 33 {y} L 40 {y+7} H 5 Z',N,2.5,LG) for y in (9,25,41))+line(52,10,52,55,B,3)+arrow(52,31,52,13,B,2)+arrow(52,35,52,52,B,2)+line(45,9,58,9,B,2)+line(45,55,58,55,B,2)
# SHEET 3
icons['DetailLine']=line(11,49,52,49,B,4)+circ(11,49,4,N,2,'white')+circ(52,49,4,N,2,'white')+path('M 25 36 L 43 11 Q 47 7 51 12 L 52 15 L 34 40 L 26 44 Z',N,3,'#E9EEF4')+path('M 26 44 L 25 36 L 34 40 Z',B,2,'#C58B59')+line(45,13,51,18,B,3)
icons['DoorDetail']=path('M 10 52 H 54 M 15 52 V 9 H 43 V 52')+path('M 20 16 L 37 12 V 49 L 20 43 Z',N,3,'#D2A073')+circ(33,32,2,N,1,'white')+path('M 35 25 L 45 14',B,2.5)+rect(42,6,16,13,B,2,'white',2)+line(45,11,54,11,N,1.5)
icons['StairSlab']=path('M 7 48 H 15 V 40 H 23 V 32 H 31 V 24 H 39 V 16 H 56 V 54 H 7 Z',N,3,LG)+path('M 16 53 L 51 28 M 27 53 L 56 32 M 38 53 L 56 41',G,2)+path('M 7 48 H 15 V 40 H 23 V 32 H 31 V 24 H 39 V 16 H 56',B,2.5)
wcbase=path('M 9 52 V 10 H 54 V 53 H 30',N,4)+path('M 9 39 Q 23 39 23 53',B,2.5)+rect(17,16,12,7,N,2,'white')+path('M 18 25 V 32 Q 23 39 28 32 V 25 Z',N,2.5,'white')+path('M 37 22 H 49 V 29 Q 43 37 37 29 Z',N,2.5,'white')+line(43,17,43,22,N,2)
icons['WcCreate']=wcbase+badge('plus')
icons['WcUpdate']=wcbase+badge('refresh')
# SHEET 4
icons['WcBatch']=rect(10,8,34,43,N,3,'white',3)+rect(15,13,34,43,N,3,'white',3)+rect(20,18,34,38,N,3,'white',2)+txt(37,39,'WC',13)+badge('stack')
icons['WireLabel']=path('M 6 34 H 20 Q 26 34 26 40 Q 26 45 33 45 H 57',N,4)+circ(26,40,3,B,2,'white')+path('M 27 37 L 38 16',B,2)+rect(34,6,24,17,B,2,'white',3)+txt(46,19,'A1',10)
icons['WireSize']=line(8,42,56,42,N,5)+circ(32,42,10,B,2,'white')+txt(32,46,'Ø',13)+line(12,13,12,29,B,2)+line(52,13,52,29,B,2)+arrow(30,20,15,20,B,2.5)+arrow(34,20,49,20,B,2.5)+txt(32,12,'Ø10',10)
icons['WireStyle']=line(12,16,52,16,N,5)+line(12,32,52,32,N,4,'stroke-dasharray="6 5"')+line(12,48,52,48,N,4,'stroke-dasharray="1 7" stroke-linecap="round"')
icons['WireTag']=path('M 6 14 H 20 Q 27 14 27 22 V 36 H 33',N,4)+circ(34,36,3,B,2,'white')+path('M 37 29 H 52 L 59 36 L 52 44 H 37 L 30 36 Z',B,2.5,'white')+txt(46,39,'TAG',9)

# Additional Ktool commands. The existing 20 icons above retain the exact source geometry.
def box_icon():return rect(9,12,46,40,N,3,LG)
def pipe_shape():return path('M 11 15 V 43 H 29 V 52 H 53',N,5)+circ(11,15,4,N,2,'white')+circ(53,52,4,N,2,'white')
def wire_shape():return path('M 7 38 H 20 L 28 28 H 40 L 47 43 H 57',N,4)+circ(7,38,3,N,2,'white')+circ(57,43,3,N,2,'white')
def page():return path('M 12 8 H 40 L 53 20 V 55 H 12 Z',N,3)+path('M 40 8 V 20 H 53',N,2)+rect(18,30,28,19,G,2,LG)
def step():return path('M 9 52 V 44 H 19 V 36 H 29 V 28 H 39 V 20 H 54 V 54 H 9 Z',N,3,LG)
icons['DrawPipe']=pipe_shape()+badge('plus')
icons['DrawWire']=wire_shape()+path('M 38 8 L 54 16 L 44 31 L 35 33 Z',B,3,LG)
icons['ExportDwg']=page()+arrow(21,27,45,27,B)
icons['FileCleanup']=page()+path('M 37 29 H 51 L 47 49 H 41 Z',B,2)+line(36,26,52,26,B,3)
for name in ['Ceiling','Floor','Menu','Threshold','Tile','Wall']:
 s=box_icon()
 if name=='Ceiling':s+=line(11,16,53,16,B,6)
 elif name=='Floor':s+=line(11,50,53,50,B,6)
 elif name=='Wall':s+=line(14,14,14,50,B,6)
 elif name=='Tile':
  for x in [24,38]:s+=line(x,13,x,51,G,2)
  for y in [26,39]:s+=line(10,y,54,y,G,2)
 elif name=='Threshold':s+=rect(24,32,18,8,B,2,LG)+arrow(33,17,33,29,B)
 else:s+=badge('stack')
 icons['Finish'+name]=s
icons['FlatLanding']=step()+line(38,20,56,20,B,5)
icons['FlipDimension']=icons['WitnessLine']+arrow(14,26,49,26,B)
icons['JoinGeometry']=rect(9,19,31,30,N,3,LG)+rect(25,10,30,31,N,3,'none')+circ(32,30,7,B,3,'white')
icons['Lintel']=rect(12,27,13,28,N,3,LG)+rect(40,27,13,28,N,3,LG)+rect(8,17,48,11,B,3,LG)
icons['MultiLevelPreset']=''.join(line(9,y,47,y,N,4)+circ(53,y,4,B,2,'white') for y in [15,30,45])
icons['Paint']=rect(10,15,40,14,N,3,LG)+path('M 50 22 V 32 H 31 V 53',N,4)+line(31,35,31,53,B,6)
pipe_codes=['CW','C','DP','HW','RP','SP','VP','WP']
for i,name in enumerate(pipe_codes):
 s=pipe_shape()+rect(32,8,26,17,B,2,'white')
 for j in range(min(i+1,4)):s+=line(36+j*5,13,36+j*5,21,N,2)
 if i>=4:s+=line(36,25,54,25,B,2)
 icons['Pipe'+name]=s
icons['PipeLabel']=pipe_shape()+path('M 27 42 L 40 19',B,2)+rect(36,6,22,15,B,2,'white')+line(40,12,54,12,N,2)
icons['PipeTag']=pipe_shape()+path('M 27 42 L 40 19',B,2)+path('M 40 6 H 58 V 22 H 40 L 34 14 Z',B,2,'white')
icons['PipeStyle']=pipe_shape()+line(17,29,51,29,B,3,'stroke-dasharray="5 4"')
icons['PipeType']=pipe_shape()+circ(44,24,9,B,2,'white')+line(39,24,49,24,N,3)
icons['PlaceView']=page()+arrow(8,29,26,29,B)+badge('plus')
icons['Plaster']=rect(12,12,32,42,N,3,LG)+line(47,13,47,53,B,6)+''.join(line(51,y,57,y+5,G,2) for y in [18,29,40])
icons['StairDeploy']=step()+arrow(8,16,42,16,B)+badge('plus')

import re
expected=[x.strip() for x in "AlignTitle AutoTag CreateSheet CropFit CropOffset CropRegion DetailLine DimensionGrid DimensionLevel DoorDetail DrawPipe DrawWire ExportDwg FileCleanup FinishCeiling FinishFloor FinishMenu FinishThreshold FinishTile FinishWall FlatLanding FlipDimension JoinGeometry Lintel MultiLevelPreset Paint PipeCW PipeC PipeDP PipeHW PipeLabel PipeRP PipeSP PipeStyle PipeTag PipeType PipeVP PipeWP PlaceView Plaster StairDeploy StairSlab ViewBrowser WcBatch WcCreate WcUpdate WireLabel WireSize WireStyle WireTag WitnessLine".split()]
assert set(icons)==set(expected),(set(expected)-set(icons),set(icons)-set(expected))
for name,shape in icons.items():
 svg=f'<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><g stroke-linecap="round" stroke-linejoin="round">{shape}</g></svg>'
 for size in (16,32):
  cairosvg.svg2png(bytestring=svg.encode(),write_to=str(OUT/f'KTool_{name}_{size}.png'),output_width=size,output_height=size)
print(f'Updated {len(icons)*2} PNG files for {len(icons)} Ktool commands in {OUT}')