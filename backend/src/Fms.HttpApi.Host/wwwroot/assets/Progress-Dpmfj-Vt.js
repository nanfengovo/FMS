import{O as e,j as t,v as n}from"./vue.runtime.esm-bundler-7zA2Yezi.js";import{at as r,it as i,nt as a,rt as o}from"./router-mOXmX71_.js";import{E as s,O as c,_n as l,dn as u,fn as d,gt as f,hn as p,ht as m,xn as h}from"./Button-CntD9bn4.js";import{R as g}from"./Switch-1iJTY2GR.js";import{r as _}from"./index-Dq9itUpf.js";var v={success:t(o,null),error:t(r,null),warning:t(a,null),info:t(i,null)},y=e({name:`ProgressCircle`,props:{clsPrefix:{type:String,required:!0},status:{type:String,required:!0},strokeWidth:{type:Number,required:!0},fillColor:[String,Object],railColor:String,railStyle:[String,Object],percentage:{type:Number,default:0},offsetDegree:{type:Number,default:0},showIndicator:{type:Boolean,required:!0},indicatorTextColor:String,unit:String,viewBoxWidth:{type:Number,required:!0},gapDegree:{type:Number,required:!0},gapOffsetDegree:{type:Number,default:0}},setup(e,{slots:r}){let i=n(()=>{let t=`gradient`,{fillColor:n}=e;return typeof n==`object`?`${t}-${h(JSON.stringify(n))}`:t});function a(t,n,r,a){let{gapDegree:o,viewBoxWidth:s,strokeWidth:c}=e,l=50+c/2,u=`M ${l},${l} m 0,50
      a 50,50 0 1 1 0,-100
      a 50,50 0 1 1 0,100`,d=Math.PI*2*50;return{pathString:u,pathStyle:{stroke:a===`rail`?r:typeof e.fillColor==`object`?`url(#${i.value})`:r,strokeDasharray:`${Math.min(t,100)/100*(d-o)}px ${s*8}px`,strokeDashoffset:`-${o/2}px`,transformOrigin:n?`center`:void 0,transform:n?`rotate(${n}deg)`:void 0}}}let o=()=>{let n=typeof e.fillColor==`object`,r=n?e.fillColor.stops[0]:``,a=n?e.fillColor.stops[1]:``;return n&&t(`defs`,null,t(`linearGradient`,{id:i.value,x1:`0%`,y1:`100%`,x2:`100%`,y2:`0%`},t(`stop`,{offset:`0%`,"stop-color":r}),t(`stop`,{offset:`100%`,"stop-color":a})))};return()=>{let{fillColor:n,railColor:i,strokeWidth:c,offsetDegree:l,status:u,percentage:d,showIndicator:f,indicatorTextColor:p,unit:m,gapOffsetDegree:h,clsPrefix:g}=e,{pathString:_,pathStyle:y}=a(100,0,i,`rail`),{pathString:b,pathStyle:x}=a(d,l,n,`fill`),S=100+c;return t(`div`,{class:`${g}-progress-content`,role:`none`},t(`div`,{class:`${g}-progress-graph`,"aria-hidden":!0},t(`div`,{class:`${g}-progress-graph-circle`,style:{transform:h?`rotate(${h}deg)`:void 0}},t(`svg`,{viewBox:`0 0 ${S} ${S}`},o(),t(`g`,null,t(`path`,{class:`${g}-progress-graph-circle-rail`,d:_,"stroke-width":c,"stroke-linecap":`round`,fill:`none`,style:y})),t(`g`,null,t(`path`,{class:[`${g}-progress-graph-circle-fill`,d===0&&`${g}-progress-graph-circle-fill--empty`],d:b,"stroke-width":c,"stroke-linecap":`round`,fill:`none`,style:x}))))),f?t(`div`,null,r.default?t(`div`,{class:`${g}-progress-custom-content`,role:`none`},r.default()):u===`default`?t(`div`,{class:`${g}-progress-text`,style:{color:p},role:`none`},t(`span`,{class:`${g}-progress-text__percentage`},d),t(`span`,{class:`${g}-progress-text__unit`},m)):t(`div`,{class:`${g}-progress-icon`,"aria-hidden":!0},t(s,{clsPrefix:g},{default:()=>v[u]}))):null)}}}),b={success:t(o,null),error:t(r,null),warning:t(a,null),info:t(i,null)},x=e({name:`ProgressLine`,props:{clsPrefix:{type:String,required:!0},percentage:{type:Number,default:0},railColor:String,railStyle:[String,Object],fillColor:[String,Object],status:{type:String,required:!0},indicatorPlacement:{type:String,required:!0},indicatorTextColor:String,unit:{type:String,default:`%`},processing:{type:Boolean,required:!0},showIndicator:{type:Boolean,required:!0},height:[String,Number],railBorderRadius:[String,Number],fillBorderRadius:[String,Number]},setup(e,{slots:r}){let i=n(()=>g(e.height)),a=n(()=>typeof e.fillColor==`object`?`linear-gradient(to right, ${e.fillColor?.stops[0]} , ${e.fillColor?.stops[1]})`:e.fillColor),o=n(()=>e.railBorderRadius===void 0?e.height===void 0?``:g(e.height,{c:.5}):g(e.railBorderRadius)),c=n(()=>e.fillBorderRadius===void 0?e.railBorderRadius===void 0?e.height===void 0?``:g(e.height,{c:.5}):g(e.railBorderRadius):g(e.fillBorderRadius));return()=>{let{indicatorPlacement:n,railColor:l,railStyle:u,percentage:d,unit:f,indicatorTextColor:p,status:m,showIndicator:h,processing:g,clsPrefix:_}=e;return t(`div`,{class:`${_}-progress-content`,role:`none`},t(`div`,{class:`${_}-progress-graph`,"aria-hidden":!0},t(`div`,{class:[`${_}-progress-graph-line`,{[`${_}-progress-graph-line--indicator-${n}`]:!0}]},t(`div`,{class:`${_}-progress-graph-line-rail`,style:[{backgroundColor:l,height:i.value,borderRadius:o.value},u]},t(`div`,{class:[`${_}-progress-graph-line-fill`,g&&`${_}-progress-graph-line-fill--processing`],style:{maxWidth:`${e.percentage}%`,background:a.value,height:i.value,lineHeight:i.value,borderRadius:c.value}},n===`inside`?t(`div`,{class:`${_}-progress-graph-line-indicator`,style:{color:p}},r.default?r.default():`${d}${f}`):null)))),h&&n===`outside`?t(`div`,null,r.default?t(`div`,{class:`${_}-progress-custom-content`,style:{color:p},role:`none`},r.default()):m===`default`?t(`div`,{role:`none`,class:`${_}-progress-icon ${_}-progress-icon--as-text`,style:{color:p}},d,f):t(`div`,{class:`${_}-progress-icon`,"aria-hidden":!0},t(s,{clsPrefix:_},{default:()=>b[m]}))):null)}}});function S(e,t,n=100){return`m ${n/2} ${n/2-e} a ${e} ${e} 0 1 1 0 ${2*e} a ${e} ${e} 0 1 1 0 -${2*e}`}var C=e({name:`ProgressMultipleCircle`,props:{clsPrefix:{type:String,required:!0},viewBoxWidth:{type:Number,required:!0},percentage:{type:Array,default:[0]},strokeWidth:{type:Number,required:!0},circleGap:{type:Number,required:!0},showIndicator:{type:Boolean,required:!0},fillColor:{type:Array,default:()=>[]},railColor:{type:Array,default:()=>[]},railStyle:{type:Array,default:()=>[]}},setup(e,{slots:r}){let i=n(()=>e.percentage.map((t,n)=>`${Math.PI*t/100*(e.viewBoxWidth/2-e.strokeWidth/2*(1+2*n)-e.circleGap*n)*2}, ${e.viewBoxWidth*8}`)),a=(n,r)=>{let i=e.fillColor[r],a=typeof i==`object`?i.stops[0]:``,o=typeof i==`object`?i.stops[1]:``;return typeof e.fillColor[r]==`object`&&t(`linearGradient`,{id:`gradient-${r}`,x1:`100%`,y1:`0%`,x2:`0%`,y2:`100%`},t(`stop`,{offset:`0%`,"stop-color":a}),t(`stop`,{offset:`100%`,"stop-color":o}))};return()=>{let{viewBoxWidth:n,strokeWidth:o,circleGap:s,showIndicator:c,fillColor:l,railColor:u,railStyle:d,percentage:f,clsPrefix:p}=e;return t(`div`,{class:`${p}-progress-content`,role:`none`},t(`div`,{class:`${p}-progress-graph`,"aria-hidden":!0},t(`div`,{class:`${p}-progress-graph-circle`},t(`svg`,{viewBox:`0 0 ${n} ${n}`},t(`defs`,null,f.map((e,t)=>a(e,t))),f.map((e,r)=>t(`g`,{key:r},t(`path`,{class:`${p}-progress-graph-circle-rail`,d:S(n/2-o/2*(1+2*r)-s*r,o,n),"stroke-width":o,"stroke-linecap":`round`,fill:`none`,style:[{strokeDashoffset:0,stroke:u[r]},d[r]]}),t(`path`,{class:[`${p}-progress-graph-circle-fill`,e===0&&`${p}-progress-graph-circle-fill--empty`],d:S(n/2-o/2*(1+2*r)-s*r,o,n),"stroke-width":o,"stroke-linecap":`round`,fill:`none`,style:{strokeDasharray:i.value[r],strokeDashoffset:0,stroke:typeof l[r]==`object`?`url(#gradient-${r})`:l[r]}})))))),c&&r.default?t(`div`,null,t(`div`,{class:`${p}-progress-text`},r.default())):null)}}}),w=u([d(`progress`,{display:`inline-block`},[d(`progress-icon`,`
 color: var(--n-icon-color);
 transition: color .3s var(--n-bezier);
 `),p(`line`,`
 width: 100%;
 display: block;
 `,[d(`progress-content`,`
 display: flex;
 align-items: center;
 `,[d(`progress-graph`,{flex:1})]),d(`progress-custom-content`,{marginLeft:`14px`}),d(`progress-icon`,`
 width: 30px;
 padding-left: 14px;
 height: var(--n-icon-size-line);
 line-height: var(--n-icon-size-line);
 font-size: var(--n-icon-size-line);
 `,[p(`as-text`,`
 color: var(--n-text-color-line-outer);
 text-align: center;
 width: 40px;
 font-size: var(--n-font-size);
 padding-left: 4px;
 transition: color .3s var(--n-bezier);
 `)])]),p(`circle, dashboard`,{width:`120px`},[d(`progress-custom-content`,`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 justify-content: center;
 `),d(`progress-text`,`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 color: inherit;
 font-size: var(--n-font-size-circle);
 color: var(--n-text-color-circle);
 font-weight: var(--n-font-weight-circle);
 transition: color .3s var(--n-bezier);
 white-space: nowrap;
 `),d(`progress-icon`,`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 color: var(--n-icon-color);
 font-size: var(--n-icon-size-circle);
 `)]),p(`multiple-circle`,`
 width: 200px;
 color: inherit;
 `,[d(`progress-text`,`
 font-weight: var(--n-font-weight-circle);
 color: var(--n-text-color-circle);
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 justify-content: center;
 transition: color .3s var(--n-bezier);
 `)]),d(`progress-content`,{position:`relative`}),d(`progress-graph`,{position:`relative`},[d(`progress-graph-circle`,[u(`svg`,{verticalAlign:`bottom`}),d(`progress-graph-circle-fill`,`
 stroke: var(--n-fill-color);
 transition:
 opacity .3s var(--n-bezier),
 stroke .3s var(--n-bezier),
 stroke-dasharray .3s var(--n-bezier);
 `,[p(`empty`,{opacity:0})]),d(`progress-graph-circle-rail`,`
 transition: stroke .3s var(--n-bezier);
 overflow: hidden;
 stroke: var(--n-rail-color);
 `)]),d(`progress-graph-line`,[p(`indicator-inside`,[d(`progress-graph-line-rail`,`
 height: 16px;
 line-height: 16px;
 border-radius: 10px;
 `,[d(`progress-graph-line-fill`,`
 height: inherit;
 border-radius: 10px;
 `),d(`progress-graph-line-indicator`,`
 background: #0000;
 white-space: nowrap;
 text-align: right;
 margin-left: 14px;
 margin-right: 14px;
 height: inherit;
 font-size: 12px;
 color: var(--n-text-color-line-inner);
 transition: color .3s var(--n-bezier);
 `)])]),p(`indicator-inside-label`,`
 height: 16px;
 display: flex;
 align-items: center;
 `,[d(`progress-graph-line-rail`,`
 flex: 1;
 transition: background-color .3s var(--n-bezier);
 `),d(`progress-graph-line-indicator`,`
 background: var(--n-fill-color);
 font-size: 12px;
 transform: translateZ(0);
 display: flex;
 vertical-align: middle;
 height: 16px;
 line-height: 16px;
 padding: 0 10px;
 border-radius: 10px;
 position: absolute;
 white-space: nowrap;
 color: var(--n-text-color-line-inner);
 transition:
 right .2s var(--n-bezier),
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
 `)]),d(`progress-graph-line-rail`,`
 position: relative;
 overflow: hidden;
 height: var(--n-rail-height);
 border-radius: 5px;
 background-color: var(--n-rail-color);
 transition: background-color .3s var(--n-bezier);
 `,[d(`progress-graph-line-fill`,`
 background: var(--n-fill-color);
 position: relative;
 border-radius: 5px;
 height: inherit;
 width: 100%;
 max-width: 0%;
 transition:
 background-color .3s var(--n-bezier),
 max-width .2s var(--n-bezier);
 `,[p(`processing`,[u(`&::after`,`
 content: "";
 background-image: var(--n-line-bg-processing);
 animation: progress-processing-animation 2s var(--n-bezier) infinite;
 `)])])])])])]),u(`@keyframes progress-processing-animation`,`
 0% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 100%;
 opacity: 1;
 }
 66% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 0;
 opacity: 0;
 }
 100% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 0;
 opacity: 0;
 }
 `)]),T=e({name:`Progress`,props:Object.assign(Object.assign({},c.props),{processing:Boolean,type:{type:String,default:`line`},gapDegree:Number,gapOffsetDegree:Number,status:{type:String,default:`default`},railColor:[String,Array],railStyle:[String,Array],color:[String,Array,Object],viewBoxWidth:{type:Number,default:100},strokeWidth:{type:Number,default:7},percentage:[Number,Array],unit:{type:String,default:`%`},showIndicator:{type:Boolean,default:!0},indicatorPosition:{type:String,default:`outside`},indicatorPlacement:{type:String,default:`outside`},indicatorTextColor:String,circleGap:{type:Number,default:1},height:Number,borderRadius:[String,Number],fillBorderRadius:[String,Number],offsetDegree:Number}),setup(e){let t=n(()=>e.indicatorPlacement||e.indicatorPosition),r=n(()=>{if(e.gapDegree||e.gapDegree===0)return e.gapDegree;if(e.type===`dashboard`)return 75}),{mergedClsPrefixRef:i,inlineThemeDisabled:a}=f(e),o=c(`Progress`,`-progress`,w,_,e,i),s=n(()=>{let{status:t}=e,{common:{cubicBezierEaseInOut:n},self:{fontSize:r,fontSizeCircle:i,railColor:a,railHeight:s,iconSizeCircle:c,iconSizeLine:u,textColorCircle:d,textColorLineInner:f,textColorLineOuter:p,lineBgProcessing:m,fontWeightCircle:h,[l(`iconColor`,t)]:g,[l(`fillColor`,t)]:_}}=o.value;return{"--n-bezier":n,"--n-fill-color":_,"--n-font-size":r,"--n-font-size-circle":i,"--n-font-weight-circle":h,"--n-icon-color":g,"--n-icon-size-circle":c,"--n-icon-size-line":u,"--n-line-bg-processing":m,"--n-rail-color":a,"--n-rail-height":s,"--n-text-color-circle":d,"--n-text-color-line-inner":f,"--n-text-color-line-outer":p}}),u=a?m(`progress`,n(()=>e.status[0]),s,e):void 0;return{mergedClsPrefix:i,mergedIndicatorPlacement:t,gapDeg:r,cssVars:a?void 0:s,themeClass:u?.themeClass,onRender:u?.onRender}},render(){let{type:e,cssVars:n,indicatorTextColor:r,showIndicator:i,status:a,railColor:o,railStyle:s,color:c,percentage:l,viewBoxWidth:u,strokeWidth:d,mergedIndicatorPlacement:f,unit:p,borderRadius:m,fillBorderRadius:h,height:g,processing:_,circleGap:v,mergedClsPrefix:b,gapDeg:S,gapOffsetDegree:w,themeClass:T,$slots:E,onRender:D}=this;return D?.(),t(`div`,{class:[T,`${b}-progress`,`${b}-progress--${e}`,`${b}-progress--${a}`],style:n,"aria-valuemax":100,"aria-valuemin":0,"aria-valuenow":l,role:e===`circle`||e===`line`||e===`dashboard`?`progressbar`:`none`},e===`circle`||e===`dashboard`?t(y,{clsPrefix:b,status:a,showIndicator:i,indicatorTextColor:r,railColor:o,fillColor:c,railStyle:s,offsetDegree:this.offsetDegree,percentage:l,viewBoxWidth:u,strokeWidth:d,gapDegree:S===void 0?e===`dashboard`?75:0:S,gapOffsetDegree:w,unit:p},E):e===`line`?t(x,{clsPrefix:b,status:a,showIndicator:i,indicatorTextColor:r,railColor:o,fillColor:c,railStyle:s,percentage:l,processing:_,indicatorPlacement:f,unit:p,fillBorderRadius:h,railBorderRadius:m,height:g},E):e===`multiple-circle`?t(C,{clsPrefix:b,strokeWidth:d,railColor:o,fillColor:c,railStyle:s,viewBoxWidth:u,percentage:l,showIndicator:i,circleGap:v},E):null)}});export{T as t};