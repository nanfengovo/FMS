import{O as e,j as t,v as n}from"./vue.runtime.esm-bundler-7zA2Yezi.js";import{O as r,St as i,fn as a,gt as o,ht as s,j as c,mn as l}from"./Button-CntD9bn4.js";import{t as u}from"./index-Ci65GCtg.js";var d=a(`statistic`,[l(`label`,`
 font-weight: var(--n-label-font-weight);
 transition: .3s color var(--n-bezier);
 font-size: var(--n-label-font-size);
 color: var(--n-label-text-color);
 `),a(`statistic-value`,`
 margin-top: 4px;
 font-weight: var(--n-value-font-weight);
 `,[l(`prefix`,`
 margin: 0 4px 0 0;
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-prefix-text-color);
 `,[a(`icon`,{verticalAlign:`-0.125em`})]),l(`content`,`
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-text-color);
 `),l(`suffix`,`
 margin: 0 0 0 4px;
 font-size: var(--n-value-font-size);
 transition: .3s color var(--n-bezier);
 color: var(--n-value-suffix-text-color);
 `,[a(`icon`,{verticalAlign:`-0.125em`})])])]),f=e({name:`Statistic`,props:Object.assign(Object.assign({},r.props),{tabularNums:Boolean,label:String,value:[String,Number]}),slots:Object,setup(e){let{mergedClsPrefixRef:t,inlineThemeDisabled:i,mergedRtlRef:a}=o(e),l=r(`Statistic`,`-statistic`,d,u,e,t),f=c(`Statistic`,a,t),p=n(()=>{let{self:{labelFontWeight:e,valueFontSize:t,valueFontWeight:n,valuePrefixTextColor:r,labelTextColor:i,valueSuffixTextColor:a,valueTextColor:o,labelFontSize:s},common:{cubicBezierEaseInOut:c}}=l.value;return{"--n-bezier":c,"--n-label-font-size":s,"--n-label-font-weight":e,"--n-label-text-color":i,"--n-value-font-weight":n,"--n-value-font-size":t,"--n-value-prefix-text-color":r,"--n-value-suffix-text-color":a,"--n-value-text-color":o}}),m=i?s(`statistic`,void 0,p,e):void 0;return{rtlEnabled:f,mergedClsPrefix:t,cssVars:i?void 0:p,themeClass:m?.themeClass,onRender:m?.onRender}},render(){var e;let{mergedClsPrefix:n,$slots:{default:r,label:a,prefix:o,suffix:s}}=this;return(e=this.onRender)==null||e.call(this),t(`div`,{class:[`${n}-statistic`,this.themeClass,this.rtlEnabled&&`${n}-statistic--rtl`],style:this.cssVars},i(a,e=>t(`div`,{class:`${n}-statistic__label`},this.label||e)),t(`div`,{class:`${n}-statistic-value`,style:{fontVariantNumeric:this.tabularNums?`tabular-nums`:``}},i(o,e=>e&&t(`span`,{class:`${n}-statistic-value__prefix`},e)),this.value===void 0?i(r,e=>e&&t(`span`,{class:`${n}-statistic-value__content`},e)):t(`span`,{class:`${n}-statistic-value__content`},this.value),i(s,e=>e&&t(`span`,{class:`${n}-statistic-value__suffix`},e))))}});export{f as t};