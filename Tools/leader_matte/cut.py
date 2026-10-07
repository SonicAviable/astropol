# Вырезка правителей из фона (GrabCut по маске силуэта + подсказки головы/фона).
# Запуск: python3 cut.py (пути — к Assets/Resources/Leaders). Пишет matte_<key>.npy и превью;
# затем маска кладётся в альфа-канал Leaders/leader_<key>_fx.png в половинном разрешении.
import cv2, numpy as np
from PIL import Image
L='/home/user/astropol/Assets/Resources/Leaders/'
for key in ('xarn','astrea','aquila'):
    img=cv2.imread(L+f'leader_{key}.jpg')
    h,w=img.shape[:2]
    fx=np.asarray(Image.open(L+f'leader_{key}_fx.png').convert('RGB')).astype(np.float32)/255
    fig=cv2.resize(fx[...,1],(w,h))
    s=0.5
    small=cv2.resize(img,(int(w*s),int(h*s)))
    f=cv2.resize(fig,(small.shape[1],small.shape[0]))
    mask=np.full(f.shape,cv2.GC_PR_BGD,np.uint8)
    mask[f>0.35]=cv2.GC_PR_FGD
    mask[f>0.92]=cv2.GC_FGD
    mask[f<0.04]=cv2.GC_BGD
    HEAD={'aquila':(680,455,282,345),'astrea':(695,360,248,272),'xarn':(650,400,225,330)}
    cx,cy,rx,ry=HEAD[key]
    yy,xx=np.mgrid[0:f.shape[0],0:f.shape[1]]
    d=((xx/s-cx)/rx)**2+((yy/s-cy)/ry)**2
    mask[(d<1.0)&(mask!=cv2.GC_FGD)]=cv2.GC_PR_FGD
    mask[d<0.72]=cv2.GC_FGD
    BG={'astrea':[(0,760,285,1450),(0,1450,195,2000),(1030,1610,1342,2000),(1255,700,1342,1580)]}
    POLY={'aquila':[(0,1100),(125,1100),(30,2000),(0,2000)]}
    if key in POLY:
        pts=(np.array(POLY[key],np.float32)*s).astype(np.int32)
        cv2.fillPoly(mask,[pts],int(cv2.GC_BGD))
    for (x0,y0,x1,y1) in BG.get(key,[]):
        mask[int(y0*s):int(y1*s),int(x0*s):int(x1*s)]=cv2.GC_BGD
    bgd=np.zeros((1,65),np.float64); fgd=np.zeros((1,65),np.float64)
    cv2.grabCut(small,mask,None,bgd,fgd,6,cv2.GC_INIT_WITH_MASK)
    m=np.where((mask==cv2.GC_FGD)|(mask==cv2.GC_PR_FGD),1.0,0.0).astype(np.float32)
    # чистка: крупнейшая связная область, заполнение дыр
    n,lab,st,_=cv2.connectedComponentsWithStats((m>0.5).astype(np.uint8))
    if n>1:
        big=1+np.argmax(st[1:,cv2.CC_STAT_AREA]); m=(lab==big).astype(np.float32)
    inv=(1-m).astype(np.uint8); n2,lab2,st2,_=cv2.connectedComponentsWithStats(inv)
    for i in range(1,n2):
        x,y,ww,hh,a=st2[i]
        if x>0 and y>0 and x+ww<m.shape[1] and y+hh<m.shape[0]: m[lab2==i]=1
    m=cv2.resize(m,(w,h),interpolation=cv2.INTER_LINEAR)
    m=cv2.GaussianBlur(m,(0,0),2.2)
    # Волосы: край мягче (растушёвка), чтобы пряди не выглядели обрезанными
    soft=cv2.GaussianBlur(m,(0,0),7.0)
    Y=np.arange(h)[:,None].astype(np.float32)
    cx,cy,rx,ry=HEAD[key]
    head=np.clip(((cy+ry*0.35)-Y)/80.0,0,1)
    m=m*(1-head)+np.maximum(m,soft)*head
    np.save(f'matte_{key}.npy',m)
    # превью: на сером и на цветном фоне
    rgb=img[...,::-1].astype(np.float32)
    bg=np.zeros_like(rgb); bg[:]=(40,60,90)
    comp=rgb*m[...,None]+bg*(1-m[...,None])
    Image.fromarray(comp.astype(np.uint8)).resize((w//3,h//3)).save(f'cut_{key}.png')
ims=[Image.open(f'cut_{k}.png') for k in ('xarn','astrea','aquila')]
o=Image.new('RGB',(sum(i.width for i in ims),ims[0].height)); x=0
for i in ims: o.paste(i,(x,0)); x+=i.width
o.save('cut_all.png')
