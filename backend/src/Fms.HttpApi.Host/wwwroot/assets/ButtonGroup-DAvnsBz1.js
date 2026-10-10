import{M as e,k as t,q as n}from"./vue.runtime.esm-bundler-BULczL5Q.js";import{Jn as r,Jt as i,Kn as a,Kt as o,Xn as s,Yn as c,qn as l,sn as u,ut as d}from"./router-DpA_sid_.js";var f=`0!important`,p=`-1px!important`;function m(e){return c(`${e}-type`,[a(`& +`,[l(`button`,{},[c(`${e}-type`,[r(`border`,{borderLeftWidth:f}),r(`state-border`,{left:p})])])])])}function h(e){return c(`${e}-type`,[a(`& +`,[l(`button`,[c(`${e}-type`,[r(`border`,{borderTopWidth:f}),r(`state-border`,{top:p})])])])])}var g=l(`button-group`,`
 flex-wrap: nowrap;
 display: inline-flex;
 position: relative;
`,[s(`vertical`,{flexDirection:`row`},[s(`rtl`,[l(`button`,[a(`&:first-child:not(:last-child)`,`
 margin-right: ${f};
 border-top-right-radius: ${f};
 border-bottom-right-radius: ${f};
 `),a(`&:last-child:not(:first-child)`,`
 margin-left: ${f};
 border-top-left-radius: ${f};
 border-bottom-left-radius: ${f};
 `),a(`&:not(:first-child):not(:last-child)`,`
 margin-left: ${f};
 margin-right: ${f};
 border-radius: ${f};
 `),m(`default`),c(`ghost`,[m(`primary`),m(`info`),m(`success`),m(`warning`),m(`error`)])])])]),c(`vertical`,{flexDirection:`column`},[l(`button`,[a(`&:first-child:not(:last-child)`,`
 margin-bottom: ${f};
 margin-left: ${f};
 margin-right: ${f};
 border-bottom-left-radius: ${f};
 border-bottom-right-radius: ${f};
 `),a(`&:last-child:not(:first-child)`,`
 margin-top: ${f};
 margin-left: ${f};
 margin-right: ${f};
 border-top-left-radius: ${f};
 border-top-right-radius: ${f};
 `),a(`&:not(:first-child):not(:last-child)`,`
 margin: ${f};
 border-radius: ${f};
 `),h(`default`),c(`ghost`,[h(`primary`),h(`info`),h(`success`),h(`warning`),h(`error`)])])])]),_=t({name:`ButtonGroup`,props:{size:String,vertical:Boolean},setup(e){let{mergedClsPrefixRef:t,mergedRtlRef:r}=u(e);return o(`-button-group`,g,t),n(d,e),{rtlEnabled:i(`ButtonGroup`,r,t),mergedClsPrefix:t}},render(){let{mergedClsPrefix:t}=this;return e(`div`,{class:[`${t}-button-group`,this.rtlEnabled&&`${t}-button-group--rtl`,this.vertical&&`${t}-button-group--vertical`],role:`group`},this.$slots)}});export{_ as t};