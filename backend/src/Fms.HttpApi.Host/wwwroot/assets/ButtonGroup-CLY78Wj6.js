import{K as e,O as t,j as n}from"./vue.runtime.esm-bundler-7zA2Yezi.js";import{a as r,dn as i,fn as a,gn as o,gt as s,hn as c,j as l,k as u,mn as d}from"./Button-CntD9bn4.js";var f=`0!important`,p=`-1px!important`;function m(e){return c(`${e}-type`,[i(`& +`,[a(`button`,{},[c(`${e}-type`,[d(`border`,{borderLeftWidth:f}),d(`state-border`,{left:p})])])])])}function h(e){return c(`${e}-type`,[i(`& +`,[a(`button`,[c(`${e}-type`,[d(`border`,{borderTopWidth:f}),d(`state-border`,{top:p})])])])])}var g=a(`button-group`,`
 flex-wrap: nowrap;
 display: inline-flex;
 position: relative;
`,[o(`vertical`,{flexDirection:`row`},[o(`rtl`,[a(`button`,[i(`&:first-child:not(:last-child)`,`
 margin-right: ${f};
 border-top-right-radius: ${f};
 border-bottom-right-radius: ${f};
 `),i(`&:last-child:not(:first-child)`,`
 margin-left: ${f};
 border-top-left-radius: ${f};
 border-bottom-left-radius: ${f};
 `),i(`&:not(:first-child):not(:last-child)`,`
 margin-left: ${f};
 margin-right: ${f};
 border-radius: ${f};
 `),m(`default`),c(`ghost`,[m(`primary`),m(`info`),m(`success`),m(`warning`),m(`error`)])])])]),c(`vertical`,{flexDirection:`column`},[a(`button`,[i(`&:first-child:not(:last-child)`,`
 margin-bottom: ${f};
 margin-left: ${f};
 margin-right: ${f};
 border-bottom-left-radius: ${f};
 border-bottom-right-radius: ${f};
 `),i(`&:last-child:not(:first-child)`,`
 margin-top: ${f};
 margin-left: ${f};
 margin-right: ${f};
 border-top-left-radius: ${f};
 border-top-right-radius: ${f};
 `),i(`&:not(:first-child):not(:last-child)`,`
 margin: ${f};
 border-radius: ${f};
 `),h(`default`),c(`ghost`,[h(`primary`),h(`info`),h(`success`),h(`warning`),h(`error`)])])])]),_=t({name:`ButtonGroup`,props:{size:String,vertical:Boolean},setup(t){let{mergedClsPrefixRef:n,mergedRtlRef:i}=s(t);return u(`-button-group`,g,n),e(r,t),{rtlEnabled:l(`ButtonGroup`,i,n),mergedClsPrefix:n}},render(){let{mergedClsPrefix:e}=this;return n(`div`,{class:[`${e}-button-group`,this.rtlEnabled&&`${e}-button-group--rtl`,this.vertical&&`${e}-button-group--vertical`],role:`group`},this.$slots)}});export{_ as t};