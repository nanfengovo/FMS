import{M as e,k as t,y as n}from"./vue.runtime.esm-bundler-BULczL5Q.js";import{Bt as r,Gt as i,Kn as a,Rt as o,Ut as s,Vt as c,Yn as l,Zn as u,bn as d,er as f,on as p,qn as m,sn as h,zt as g}from"./router-CpV6ijuq.js";import{r as _}from"./index-BtDbCVHj.js";var v={success:e(g,null),error:e(c,null),warning:e(o,null),info:e(r,null)},y=t({name:`ProgressCircle`,props:{clsPrefix:{type:String,required:!0},status:{type:String,required:!0},strokeWidth:{type:Number,required:!0},fillColor:[String,Object],railColor:String,railStyle:[String,Object],percentage:{type:Number,default:0},offsetDegree:{type:Number,default:0},showIndicator:{type:Boolean,required:!0},indicatorTextColor:String,unit:String,viewBoxWidth:{type:Number,required:!0},gapDegree:{type:Number,required:!0},gapOffsetDegree:{type:Number,default:0}},setup(t,{slots:r}){let i=n(()=>{let e=`gradient`,{fillColor:n}=t;return typeof n==`object`?`${e}-${f(JSON.stringify(n))}`:e});function a(e,n,r,a){let{gapDegree:o,viewBoxWidth:s,strokeWidth:c}=t,l=50+c/2,u=`M ${l},${l} m 0,50
      a 50,50 0 1 1 0,-100
      a 50,50 0 1 1 0,100`,d=Math.PI*2*50;return{pathString:u,pathStyle:{stroke:a===`rail`?r:typeof t.fillColor==`object`?`url(#${i.value})`:r,strokeDasharray:`${Math.min(e,100)/100*(d-o)}px ${s*8}px`,strokeDashoffset:`-${o/2}px`,transformOrigin:n?`center`:void 0,transform:n?`rotate(${n}deg)`:void 0}}}let o=()=>{let n=typeof t.fillColor==`object`,r=n?t.fillColor.stops[0]:``,a=n?t.fillColor.stops[1]:``;return n&&e(`defs`,null,e(`linearGradient`,{id:i.value,x1:`0%`,y1:`100%`,x2:`100%`,y2:`0%`},e(`stop`,{offset:`0%`,"stop-color":r}),e(`stop`,{offset:`100%`,"stop-color":a})))};return()=>{let{fillColor:n,railColor:i,strokeWidth:c,offsetDegree:l,status:u,percentage:d,showIndicator:f,indicatorTextColor:p,unit:m,gapOffsetDegree:h,clsPrefix:g}=t,{pathString:_,pathStyle:y}=a(100,0,i,`rail`),{pathString:b,pathStyle:x}=a(d,l,n,`fill`),S=100+c;return e(`div`,{class:`${g}-progress-content`,role:`none`},e(`div`,{class:`${g}-progress-graph`,"aria-hidden":!0},e(`div`,{class:`${g}-progress-graph-circle`,style:{transform:h?`rotate(${h}deg)`:void 0}},e(`svg`,{viewBox:`0 0 ${S} ${S}`},o(),e(`g`,null,e(`path`,{class:`${g}-progress-graph-circle-rail`,d:_,"stroke-width":c,"stroke-linecap":`round`,fill:`none`,style:y})),e(`g`,null,e(`path`,{class:[`${g}-progress-graph-circle-fill`,d===0&&`${g}-progress-graph-circle-fill--empty`],d:b,"stroke-width":c,"stroke-linecap":`round`,fill:`none`,style:x}))))),f?e(`div`,null,r.default?e(`div`,{class:`${g}-progress-custom-content`,role:`none`},r.default()):u===`default`?e(`div`,{class:`${g}-progress-text`,style:{color:p},role:`none`},e(`span`,{class:`${g}-progress-text__percentage`},d),e(`span`,{class:`${g}-progress-text__unit`},m)):e(`div`,{class:`${g}-progress-icon`,"aria-hidden":!0},e(s,{clsPrefix:g},{default:()=>v[u]}))):null)}}}),b={success:e(g,null),error:e(c,null),warning:e(o,null),info:e(r,null)},x=t({name:`ProgressLine`,props:{clsPrefix:{type:String,required:!0},percentage:{type:Number,default:0},railColor:String,railStyle:[String,Object],fillColor:[String,Object],status:{type:String,required:!0},indicatorPlacement:{type:String,required:!0},indicatorTextColor:String,unit:{type:String,default:`%`},processing:{type:Boolean,required:!0},showIndicator:{type:Boolean,required:!0},height:[String,Number],railBorderRadius:[String,Number],fillBorderRadius:[String,Number]},setup(t,{slots:r}){let i=n(()=>d(t.height)),a=n(()=>typeof t.fillColor==`object`?`linear-gradient(to right, ${t.fillColor?.stops[0]} , ${t.fillColor?.stops[1]})`:t.fillColor),o=n(()=>t.railBorderRadius===void 0?t.height===void 0?``:d(t.height,{c:.5}):d(t.railBorderRadius)),c=n(()=>t.fillBorderRadius===void 0?t.railBorderRadius===void 0?t.height===void 0?``:d(t.height,{c:.5}):d(t.railBorderRadius):d(t.fillBorderRadius));return()=>{let{indicatorPlacement:n,railColor:l,railStyle:u,percentage:d,unit:f,indicatorTextColor:p,status:m,showIndicator:h,processing:g,clsPrefix:_}=t;return e(`div`,{class:`${_}-progress-content`,role:`none`},e(`div`,{class:`${_}-progress-graph`,"aria-hidden":!0},e(`div`,{class:[`${_}-progress-graph-line`,{[`${_}-progress-graph-line--indicator-${n}`]:!0}]},e(`div`,{class:`${_}-progress-graph-line-rail`,style:[{backgroundColor:l,height:i.value,borderRadius:o.value},u]},e(`div`,{class:[`${_}-progress-graph-line-fill`,g&&`${_}-progress-graph-line-fill--processing`],style:{maxWidth:`${t.percentage}%`,background:a.value,height:i.value,lineHeight:i.value,borderRadius:c.value}},n===`inside`?e(`div`,{class:`${_}-progress-graph-line-indicator`,style:{color:p}},r.default?r.default():`${d}${f}`):null)))),h&&n===`outside`?e(`div`,null,r.default?e(`div`,{class:`${_}-progress-custom-content`,style:{color:p},role:`none`},r.default()):m===`default`?e(`div`,{role:`none`,class:`${_}-progress-icon ${_}-progress-icon--as-text`,style:{color:p}},d,f):e(`div`,{class:`${_}-progress-icon`,"aria-hidden":!0},e(s,{clsPrefix:_},{default:()=>b[m]}))):null)}}});function S(e,t,n=100){return`m ${n/2} ${n/2-e} a ${e} ${e} 0 1 1 0 ${2*e} a ${e} ${e} 0 1 1 0 -${2*e}`}var C=t({name:`ProgressMultipleCircle`,props:{clsPrefix:{type:String,required:!0},viewBoxWidth:{type:Number,required:!0},percentage:{type:Array,default:[0]},strokeWidth:{type:Number,required:!0},circleGap:{type:Number,required:!0},showIndicator:{type:Boolean,required:!0},fillColor:{type:Array,default:()=>[]},railColor:{type:Array,default:()=>[]},railStyle:{type:Array,default:()=>[]}},setup(t,{slots:r}){let i=n(()=>t.percentage.map((e,n)=>`${Math.PI*e/100*(t.viewBoxWidth/2-t.strokeWidth/2*(1+2*n)-t.circleGap*n)*2}, ${t.viewBoxWidth*8}`)),a=(n,r)=>{let i=t.fillColor[r],a=typeof i==`object`?i.stops[0]:``,o=typeof i==`object`?i.stops[1]:``;return typeof t.fillColor[r]==`object`&&e(`linearGradient`,{id:`gradient-${r}`,x1:`100%`,y1:`0%`,x2:`0%`,y2:`100%`},e(`stop`,{offset:`0%`,"stop-color":a}),e(`stop`,{offset:`100%`,"stop-color":o}))};return()=>{let{viewBoxWidth:n,strokeWidth:o,circleGap:s,showIndicator:c,fillColor:l,railColor:u,railStyle:d,percentage:f,clsPrefix:p}=t;return e(`div`,{class:`${p}-progress-content`,role:`none`},e(`div`,{class:`${p}-progress-graph`,"aria-hidden":!0},e(`div`,{class:`${p}-progress-graph-circle`},e(`svg`,{viewBox:`0 0 ${n} ${n}`},e(`defs`,null,f.map((e,t)=>a(e,t))),f.map((t,r)=>e(`g`,{key:r},e(`path`,{class:`${p}-progress-graph-circle-rail`,d:S(n/2-o/2*(1+2*r)-s*r,o,n),"stroke-width":o,"stroke-linecap":`round`,fill:`none`,style:[{strokeDashoffset:0,stroke:u[r]},d[r]]}),e(`path`,{class:[`${p}-progress-graph-circle-fill`,t===0&&`${p}-progress-graph-circle-fill--empty`],d:S(n/2-o/2*(1+2*r)-s*r,o,n),"stroke-width":o,"stroke-linecap":`round`,fill:`none`,style:{strokeDasharray:i.value[r],strokeDashoffset:0,stroke:typeof l[r]==`object`?`url(#gradient-${r})`:l[r]}})))))),c&&r.default?e(`div`,null,e(`div`,{class:`${p}-progress-text`},r.default())):null)}}}),w=a([m(`progress`,{display:`inline-block`},[m(`progress-icon`,`
 color: var(--n-icon-color);
 transition: color .3s var(--n-bezier);
 `),l(`line`,`
 width: 100%;
 display: block;
 `,[m(`progress-content`,`
 display: flex;
 align-items: center;
 `,[m(`progress-graph`,{flex:1})]),m(`progress-custom-content`,{marginLeft:`14px`}),m(`progress-icon`,`
 width: 30px;
 padding-left: 14px;
 height: var(--n-icon-size-line);
 line-height: var(--n-icon-size-line);
 font-size: var(--n-icon-size-line);
 `,[l(`as-text`,`
 color: var(--n-text-color-line-outer);
 text-align: center;
 width: 40px;
 font-size: var(--n-font-size);
 padding-left: 4px;
 transition: color .3s var(--n-bezier);
 `)])]),l(`circle, dashboard`,{width:`120px`},[m(`progress-custom-content`,`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 justify-content: center;
 `),m(`progress-text`,`
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
 `),m(`progress-icon`,`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 color: var(--n-icon-color);
 font-size: var(--n-icon-size-circle);
 `)]),l(`multiple-circle`,`
 width: 200px;
 color: inherit;
 `,[m(`progress-text`,`
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
 `)]),m(`progress-content`,{position:`relative`}),m(`progress-graph`,{position:`relative`},[m(`progress-graph-circle`,[a(`svg`,{verticalAlign:`bottom`}),m(`progress-graph-circle-fill`,`
 stroke: var(--n-fill-color);
 transition:
 opacity .3s var(--n-bezier),
 stroke .3s var(--n-bezier),
 stroke-dasharray .3s var(--n-bezier);
 `,[l(`empty`,{opacity:0})]),m(`progress-graph-circle-rail`,`
 transition: stroke .3s var(--n-bezier);
 overflow: hidden;
 stroke: var(--n-rail-color);
 `)]),m(`progress-graph-line`,[l(`indicator-inside`,[m(`progress-graph-line-rail`,`
 height: 16px;
 line-height: 16px;
 border-radius: 10px;
 `,[m(`progress-graph-line-fill`,`
 height: inherit;
 border-radius: 10px;
 `),m(`progress-graph-line-indicator`,`
 background: #0000;
 white-space: nowrap;
 text-align: right;
 margin-left: 14px;
 margin-right: 14px;
 height: inherit;
 font-size: 12px;
 color: var(--n-text-color-line-inner);
 transition: color .3s var(--n-bezier);
 `)])]),l(`indicator-inside-label`,`
 height: 16px;
 display: flex;
 align-items: center;
 `,[m(`progress-graph-line-rail`,`
 flex: 1;
 transition: background-color .3s var(--n-bezier);
 `),m(`progress-graph-line-indicator`,`
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
 `)]),m(`progress-graph-line-rail`,`
 position: relative;
 overflow: hidden;
 height: var(--n-rail-height);
 border-radius: 5px;
 background-color: var(--n-rail-color);
 transition: background-color .3s var(--n-bezier);
 `,[m(`progress-graph-line-fill`,`
 background: var(--n-fill-color);
 position: relative;
 border-radius: 5px;
 height: inherit;
 width: 100%;
 max-width: 0%;
 transition:
 background-color .3s var(--n-bezier),
 max-width .2s var(--n-bezier);
 `,[l(`processing`,[a(`&::after`,`
 content: "";
 background-image: var(--n-line-bg-processing);
 animation: progress-processing-animation 2s var(--n-bezier) infinite;
 `)])])])])])]),a(`@keyframes progress-processing-animation`,`
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
 `)]),T=t({name:`Progress`,props:Object.assign(Object.assign({},i.props),{processing:Boolean,type:{type:String,default:`line`},gapDegree:Number,gapOffsetDegree:Number,status:{type:String,default:`default`},railColor:[String,Array],railStyle:[String,Array],color:[String,Array,Object],viewBoxWidth:{type:Number,default:100},strokeWidth:{type:Number,default:7},percentage:[Number,Array],unit:{type:String,default:`%`},showIndicator:{type:Boolean,default:!0},indicatorPosition:{type:String,default:`outside`},indicatorPlacement:{type:String,default:`outside`},indicatorTextColor:String,circleGap:{type:Number,default:1},height:Number,borderRadius:[String,Number],fillBorderRadius:[String,Number],offsetDegree:Number}),setup(e){let t=n(()=>e.indicatorPlacement||e.indicatorPosition),r=n(()=>{if(e.gapDegree||e.gapDegree===0)return e.gapDegree;if(e.type===`dashboard`)return 75}),{mergedClsPrefixRef:a,inlineThemeDisabled:o}=h(e),s=i(`Progress`,`-progress`,w,_,e,a),c=n(()=>{let{status:t}=e,{common:{cubicBezierEaseInOut:n},self:{fontSize:r,fontSizeCircle:i,railColor:a,railHeight:o,iconSizeCircle:c,iconSizeLine:l,textColorCircle:d,textColorLineInner:f,textColorLineOuter:p,lineBgProcessing:m,fontWeightCircle:h,[u(`iconColor`,t)]:g,[u(`fillColor`,t)]:_}}=s.value;return{"--n-bezier":n,"--n-fill-color":_,"--n-font-size":r,"--n-font-size-circle":i,"--n-font-weight-circle":h,"--n-icon-color":g,"--n-icon-size-circle":c,"--n-icon-size-line":l,"--n-line-bg-processing":m,"--n-rail-color":a,"--n-rail-height":o,"--n-text-color-circle":d,"--n-text-color-line-inner":f,"--n-text-color-line-outer":p}}),l=o?p(`progress`,n(()=>e.status[0]),c,e):void 0;return{mergedClsPrefix:a,mergedIndicatorPlacement:t,gapDeg:r,cssVars:o?void 0:c,themeClass:l?.themeClass,onRender:l?.onRender}},render(){let{type:t,cssVars:n,indicatorTextColor:r,showIndicator:i,status:a,railColor:o,railStyle:s,color:c,percentage:l,viewBoxWidth:u,strokeWidth:d,mergedIndicatorPlacement:f,unit:p,borderRadius:m,fillBorderRadius:h,height:g,processing:_,circleGap:v,mergedClsPrefix:b,gapDeg:S,gapOffsetDegree:w,themeClass:T,$slots:E,onRender:D}=this;return D?.(),e(`div`,{class:[T,`${b}-progress`,`${b}-progress--${t}`,`${b}-progress--${a}`],style:n,"aria-valuemax":100,"aria-valuemin":0,"aria-valuenow":l,role:t===`circle`||t===`line`||t===`dashboard`?`progressbar`:`none`},t===`circle`||t===`dashboard`?e(y,{clsPrefix:b,status:a,showIndicator:i,indicatorTextColor:r,railColor:o,fillColor:c,railStyle:s,offsetDegree:this.offsetDegree,percentage:l,viewBoxWidth:u,strokeWidth:d,gapDegree:S===void 0?t===`dashboard`?75:0:S,gapOffsetDegree:w,unit:p},E):t===`line`?e(x,{clsPrefix:b,status:a,showIndicator:i,indicatorTextColor:r,railColor:o,fillColor:c,railStyle:s,percentage:l,processing:_,indicatorPlacement:f,unit:p,fillBorderRadius:h,railBorderRadius:m,height:g},E):t===`multiple-circle`?e(C,{clsPrefix:b,strokeWidth:d,railColor:o,fillColor:c,railStyle:s,viewBoxWidth:u,percentage:l,showIndicator:i,circleGap:v},E):null)}});export{T as t};