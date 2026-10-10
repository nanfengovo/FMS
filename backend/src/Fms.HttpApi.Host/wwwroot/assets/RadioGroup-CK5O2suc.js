import{M as e,P as t,k as n,mt as r,q as i,y as a,yt as o}from"./vue.runtime.esm-bundler-BULczL5Q.js";import{Fn as s,Gt as c,Jn as l,Jt as u,Kn as d,On as f,Xn as p,Yn as m,Zn as h,an as g,gn as _,hn as v,jn as y,on as b,qn as x,sn as S,un as C}from"./router-DpA_sid_.js";import{n as w}from"./Space-wGPPVEce.js";import{c as T}from"./index-iiLhgaIU.js";var E={name:String,value:{type:[String,Number,Boolean],default:`on`},checked:{type:Boolean,default:void 0},defaultChecked:Boolean,disabled:{type:Boolean,default:void 0},label:String,size:String,onUpdateChecked:[Function,Array],"onUpdate:checked":[Function,Array],checkedValue:{type:Boolean,default:void 0}},D=f(`n-radio-group`);function O(e){let n=t(D,null),{mergedClsPrefixRef:i,mergedComponentPropsRef:a}=S(e),c=g(e,{mergedSize(t){let{size:r}=e;if(r!==void 0)return r;if(n){let{mergedSizeRef:{value:e}}=n;if(e!==void 0)return e}return t?t.mergedSize.value:a?.value?.Radio?.size||`medium`},mergedDisabled(t){return!!(e.disabled||n?.disabledRef.value||t?.disabled.value)}}),{mergedSizeRef:l,mergedDisabledRef:u}=c,d=r(null),f=r(null),p=r(e.defaultChecked),m=y(o(e,`checked`),p),h=s(()=>n?n.valueRef.value===e.value:m.value),v=s(()=>{let{name:t}=e;if(t!==void 0)return t;if(n)return n.nameRef.value}),b=r(!1);function x(){if(n){let{doUpdateValue:t}=n,{value:r}=e;_(t,r)}else{let{onUpdateChecked:t,"onUpdate:checked":n}=e,{nTriggerFormInput:r,nTriggerFormChange:i}=c;t&&_(t,!0),n&&_(n,!0),r(),i(),p.value=!0}}function C(){u.value||h.value||x()}function w(){C(),d.value&&(d.value.checked=h.value)}function T(){b.value=!1}function E(){b.value=!0}return{mergedClsPrefix:n?n.mergedClsPrefixRef:i,inputRef:d,labelRef:f,mergedName:v,mergedDisabled:u,renderSafeChecked:h,focus:b,mergedSize:l,handleRadioInputChange:w,handleRadioInputBlur:T,handleRadioInputFocus:E}}var k=n({name:`RadioButton`,props:E,setup:O,render(){let{mergedClsPrefix:t}=this;return e(`label`,{class:[`${t}-radio-button`,this.mergedDisabled&&`${t}-radio-button--disabled`,this.renderSafeChecked&&`${t}-radio-button--checked`,this.focus&&[`${t}-radio-button--focus`]]},e(`input`,{ref:`inputRef`,type:`radio`,class:`${t}-radio-input`,value:this.value,name:this.mergedName,checked:this.renderSafeChecked,disabled:this.mergedDisabled,onChange:this.handleRadioInputChange,onFocus:this.handleRadioInputFocus,onBlur:this.handleRadioInputBlur}),e(`div`,{class:`${t}-radio-button__state-border`}),C(this.$slots.default,n=>!n&&!this.label?null:e(`div`,{ref:`labelRef`,class:`${t}-radio__label`},n||this.label)))}}),A=x(`radio-group`,`
 display: inline-block;
 font-size: var(--n-font-size);
`,[l(`splitor`,`
 display: inline-block;
 vertical-align: bottom;
 width: 1px;
 transition:
 background-color .3s var(--n-bezier),
 opacity .3s var(--n-bezier);
 background: var(--n-button-border-color);
 `,[m(`checked`,{backgroundColor:`var(--n-button-border-color-active)`}),m(`disabled`,{opacity:`var(--n-opacity-disabled)`})]),m(`button-group`,`
 white-space: nowrap;
 height: var(--n-height);
 line-height: var(--n-height);
 `,[x(`radio-button`,{height:`var(--n-height)`,lineHeight:`var(--n-height)`}),l(`splitor`,{height:`var(--n-height)`})]),x(`radio-button`,`
 vertical-align: bottom;
 outline: none;
 position: relative;
 user-select: none;
 -webkit-user-select: none;
 display: inline-block;
 box-sizing: border-box;
 padding-left: 14px;
 padding-right: 14px;
 white-space: nowrap;
 transition:
 background-color .3s var(--n-bezier),
 opacity .3s var(--n-bezier),
 border-color .3s var(--n-bezier),
 color .3s var(--n-bezier);
 background: var(--n-button-color);
 color: var(--n-button-text-color);
 border-top: 1px solid var(--n-button-border-color);
 border-bottom: 1px solid var(--n-button-border-color);
 `,[x(`radio-input`,`
 pointer-events: none;
 position: absolute;
 border: 0;
 border-radius: inherit;
 left: 0;
 right: 0;
 top: 0;
 bottom: 0;
 opacity: 0;
 z-index: 1;
 `),l(`state-border`,`
 z-index: 1;
 pointer-events: none;
 position: absolute;
 box-shadow: var(--n-button-box-shadow);
 transition: box-shadow .3s var(--n-bezier);
 left: -1px;
 bottom: -1px;
 right: -1px;
 top: -1px;
 `),d(`&:first-child`,`
 border-top-left-radius: var(--n-button-border-radius);
 border-bottom-left-radius: var(--n-button-border-radius);
 border-left: 1px solid var(--n-button-border-color);
 `,[l(`state-border`,`
 border-top-left-radius: var(--n-button-border-radius);
 border-bottom-left-radius: var(--n-button-border-radius);
 `)]),d(`&:last-child`,`
 border-top-right-radius: var(--n-button-border-radius);
 border-bottom-right-radius: var(--n-button-border-radius);
 border-right: 1px solid var(--n-button-border-color);
 `,[l(`state-border`,`
 border-top-right-radius: var(--n-button-border-radius);
 border-bottom-right-radius: var(--n-button-border-radius);
 `)]),p(`disabled`,`
 cursor: pointer;
 `,[d(`&:hover`,[l(`state-border`,`
 transition: box-shadow .3s var(--n-bezier);
 box-shadow: var(--n-button-box-shadow-hover);
 `),p(`checked`,{color:`var(--n-button-text-color-hover)`})]),m(`focus`,[d(`&:not(:active)`,[l(`state-border`,{boxShadow:`var(--n-button-box-shadow-focus)`})])])]),m(`checked`,`
 background: var(--n-button-color-active);
 color: var(--n-button-text-color-active);
 border-color: var(--n-button-border-color-active);
 `),m(`disabled`,`
 cursor: not-allowed;
 opacity: var(--n-opacity-disabled);
 `)])]);function j(t,n,r){let i=[],a=!1;for(let o=0;o<t.length;++o){let s=t[o],c=s.type?.name;c===`RadioButton`&&(a=!0);let l=s.props;if(c!==`RadioButton`){i.push(s);continue}if(o===0)i.push(s);else{let t=i[i.length-1].props,a=n===t.value,o=t.disabled,c=n===l.value,u=l.disabled,d=(a?2:0)+ +!o,f=(c?2:0)+ +!u,p={[`${r}-radio-group__splitor--disabled`]:o,[`${r}-radio-group__splitor--checked`]:a},m={[`${r}-radio-group__splitor--disabled`]:u,[`${r}-radio-group__splitor--checked`]:c},h=d<f?m:p;i.push(e(`div`,{class:[`${r}-radio-group__splitor`,h]}),s)}}return{children:i,isButtonGroup:a}}var M=n({name:`RadioGroup`,props:Object.assign(Object.assign({},c.props),{name:String,value:[String,Number,Boolean],defaultValue:{type:[String,Number,Boolean],default:null},size:String,disabled:{type:Boolean,default:void 0},"onUpdate:value":[Function,Array],onUpdateValue:[Function,Array]}),setup(e){let t=r(null),{mergedSizeRef:n,mergedDisabledRef:s,nTriggerFormChange:l,nTriggerFormInput:d,nTriggerFormBlur:f,nTriggerFormFocus:p}=g(e),{mergedClsPrefixRef:m,inlineThemeDisabled:v,mergedRtlRef:x}=S(e),C=c(`Radio`,`-radio-group`,A,T,e,m),w=r(e.defaultValue),E=y(o(e,`value`),w);function O(t){let{onUpdateValue:n,"onUpdate:value":r}=e;n&&_(n,t),r&&_(r,t),w.value=t,l(),d()}function k(e){let{value:n}=t;n&&(n.contains(e.relatedTarget)||p())}function j(e){let{value:n}=t;n&&(n.contains(e.relatedTarget)||f())}i(D,{mergedClsPrefixRef:m,nameRef:o(e,`name`),valueRef:E,disabledRef:s,mergedSizeRef:n,doUpdateValue:O});let M=u(`Radio`,x,m),N=a(()=>{let{value:e}=n,{common:{cubicBezierEaseInOut:t},self:{buttonBorderColor:r,buttonBorderColorActive:i,buttonBorderRadius:a,buttonBoxShadow:o,buttonBoxShadowFocus:s,buttonBoxShadowHover:c,buttonColor:l,buttonColorActive:u,buttonTextColor:d,buttonTextColorActive:f,buttonTextColorHover:p,opacityDisabled:m,[h(`buttonHeight`,e)]:g,[h(`fontSize`,e)]:_}}=C.value;return{"--n-font-size":_,"--n-bezier":t,"--n-button-border-color":r,"--n-button-border-color-active":i,"--n-button-border-radius":a,"--n-button-box-shadow":o,"--n-button-box-shadow-focus":s,"--n-button-box-shadow-hover":c,"--n-button-color":l,"--n-button-color-active":u,"--n-button-text-color":d,"--n-button-text-color-hover":p,"--n-button-text-color-active":f,"--n-height":g,"--n-opacity-disabled":m}}),P=v?b(`radio-group`,a(()=>n.value[0]),N,e):void 0;return{selfElRef:t,rtlEnabled:M,mergedClsPrefix:m,mergedValue:E,handleFocusout:j,handleFocusin:k,cssVars:v?void 0:N,themeClass:P?.themeClass,onRender:P?.onRender}},render(){var t;let{mergedValue:n,mergedClsPrefix:r,handleFocusin:i,handleFocusout:a}=this,{children:o,isButtonGroup:s}=j(v(w(this)),n,r);return(t=this.onRender)==null||t.call(this),e(`div`,{onFocusin:i,onFocusout:a,ref:`selfElRef`,class:[`${r}-radio-group`,this.rtlEnabled&&`${r}-radio-group--rtl`,this.themeClass,s&&`${r}-radio-group--button-group`],style:this.cssVars},o)}});export{k as n,M as t};