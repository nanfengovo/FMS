import{M as e,k as t,y as n}from"./vue.runtime.esm-bundler-BULczL5Q.js";import{Gt as r,Jn as i,Jt as a,on as o,qn as s,sn as c,un as l}from"./router-DpA_sid_.js";import{t as u}from"./index-iiLhgaIU.js";var d=s(`statistic`,[i(`label`,`
 font-weight: var(--n-label-font-weight);
 transition: .3s color var(--n-bezier);
 font-size: var(--n-label-font-size);
 color: var(--n-label-text-color);
 `),s(`statistic-value`,`
 margin-top: 4px;
 font-weight: var(--n-value-font-weight);
 `,[i(`prefix`,`
 margin: 0 4px 0 0;
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-prefix-text-color);
 `,[s(`icon`,{verticalAlign:`-0.125em`})]),i(`content`,`
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-text-color);
 `),i(`suffix`,`
 margin: 0 0 0 4px;
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-suffix-text-color);
 `,[s(`icon`,{verticalAlign:`-0.125em`})])])]),f=t({name:`Statistic`,props:Object.assign(Object.assign({},r.props),{tabularNums:Boolean,label:String,value:[String,Number]}),slots:Object,setup(e){let{mergedClsPrefixRef:t,inlineThemeDisabled:i,mergedRtlRef:s}=c(e),l=r(`Statistic`,`-statistic`,d,u,e,t),f=a(`Statistic`,s,t),p=n(()=>{let{self:{labelFontWeight:e,valueFontSize:t,valueFontWeight:n,valuePrefixTextColor:r,labelTextColor:i,valueSuffixTextColor:a,valueTextColor:o,labelFontSize:s},common:{cubicBezierEaseInOut:c}}=l.value;return{"--n-bezier":c,"--n-label-font-size":s,"--n-label-font-weight":e,"--n-label-text-color":i,"--n-value-font-weight":n,"--n-value-font-size":t,"--n-value-prefix-text-color":r,"--n-value-suffix-text-color":a,"--n-value-text-color":o}}),m=i?o(`statistic`,void 0,p,e):void 0;return{rtlEnabled:f,mergedClsPrefix:t,cssVars:i?void 0:p,themeClass:m?.themeClass,onRender:m?.onRender}},render(){var t;let{mergedClsPrefix:n,$slots:{default:r,label:i,prefix:a,suffix:o}}=this;return(t=this.onRender)==null||t.call(this),e(`div`,{class:[`${n}-statistic`,this.themeClass,this.rtlEnabled&&`${n}-statistic--rtl`],style:this.cssVars},l(i,t=>e(`div`,{class:`${n}-statistic__label`},this.label||t)),e(`div`,{class:`${n}-statistic-value`,style:{fontVariantNumeric:this.tabularNums?`tabular-nums`:``}},l(a,t=>t&&e(`span`,{class:`${n}-statistic-value__prefix`},t)),this.value===void 0?l(r,t=>t&&e(`span`,{class:`${n}-statistic-value__content`},t)):e(`span`,{class:`${n}-statistic-value__content`},this.value),l(o,t=>t&&e(`span`,{class:`${n}-statistic-value__suffix`},t))))}});export{f as t};