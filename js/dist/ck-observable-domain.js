!function(e,t){"object"==typeof exports&&"object"==typeof module?module.exports=t():"function"==typeof define&&define.amd?define([],t):"object"==typeof exports?exports.ckObservableDomain=t():e.ckObservableDomain=t()}(this,function(){return function(e){var t={};function r(n){if(t[n])return t[n].exports;var i=t[n]={i:n,l:!1,exports:{}};return e[n].call(i.exports,i,i.exports,r),i.l=!0,i.exports}return r.m=e,r.c=t,r.d=function(e,t,n){r.o(e,t)||Object.defineProperty(e,t,{enumerable:!0,get:n})},r.r=function(e){"undefined"!=typeof Symbol&&Symbol.toStringTag&&Object.defineProperty(e,Symbol.toStringTag,{value:"Module"}),Object.defineProperty(e,"__esModule",{value:!0})},r.t=function(e,t){if(1&t&&(e=r(e)),8&t)return e;if(4&t&&"object"==typeof e&&e&&e.__esModule)return e;var n=Object.create(null);if(r.r(n),Object.defineProperty(n,"default",{enumerable:!0,value:e}),2&t&&"string"!=typeof e)for(var i in e)r.d(n,i,function(t){return e[t]}.bind(null,i));return n},r.n=function(e){var t=e&&e.__esModule?function(){return e.default}:function(){return e};return r.d(t,"a",t),t},r.o=function(e,t){return Object.prototype.hasOwnProperty.call(e,t)},r.p="",r(r.s=0)}([function(e,t,r){"use strict";Object.defineProperty(t,"__esModule",{value:!0});var n=r(1);var i=function(){function e(e){var t=this;this.o="string"==typeof e?n.deserialize(e,{prefix:""}):e,this.props=this.o.P,this.tranNum=this.o.N,this.graph=function(e){for(var t=e.length,r=0;r<t;++r){var n=e[r];if(n&&"object"==typeof n)for(var i in n){var o=n[i];o&&"object"==typeof o&&void 0!==o.$C&&(n[i]=e[o.$i])}}return e}(this.o.O),this.roots=this.o.R.map(function(e){return t.graph[e]})}return e.prototype.applyEvent=function(e){if(this.tranNum+1>e.N)throw new Error("Invalid transaction number. Expected: greater than "+(this.tranNum+1)+", got "+e.N+".");for(var t=e.E,r=0;r<t.length;++r){var n=t[r];switch(n[0]){case"N":var i=void 0;switch(n[2]){case"":i={};break;case"A":i=[];break;case"M":i=new Map;break;case"S":i=new Set;break;default:throw new Error("Unexpected Object type; "+n[2]+". Must be A, M, S or empty string.")}this.graph[n[1]]=i;break;case"D":this.graph[n[1]]=null;break;case"P":if(n[2]!=this.props.length)throw new Error("Invalid property creation event for '"+n[1]+"': index must be "+this.props.length+", got "+n[2]+".");this.props.push(n[1]);break;case"C":this.graph[n[1]][this.props[n[2]]]=this.getValue(n[3]);break;case"I":var o=this.graph[n[1]],a=n[2],s=this.getValue(n[3]);a===o.length?o[a]=s:o.splice(a,0,s);break;case"CL":var f=this.graph[n[1]];f instanceof Array?f.length=0:f.clear();break;case"R":this.graph[n[1]].splice(n[2],1);break;case"S":this.graph[n[1]].splice(n[2],1,this.getValue(n[3]));break;case"K":this.graph[n[1]].delete(this.getValue(n[2]));break;default:throw new Error("Unexpected Event code: '"+n[0]+"'.")}}this.tranNum=e.N},e.prototype.getValue=function(e){if(null!=e){var t=e[">"];if(void 0!==t)return this.graph[t]}return e},e}();t.ObservableDomain=i},function(e,t,r){"use strict";function n(e,t){const{prefix:r,substitor:n}=Object.assign({prefix:"~$£€"},t),i=r+">",o=r+"°",a=r+"þ",s=Symbol();let f=[];try{let t=0;function c(e){return e[s]=1,e}return JSON.stringify(e,function(e,r){if(e===i||e===o||e===a){if(null!==r&&r[s])return r;throw new Error("Conflicting serialization prefix: property '"+e+"' exists.")}if(null===r||"object"!=typeof r||r[s])return r;let u=r[o];if(u){if(u[s])return{[i]:u};throw new Error("Conflicting serialization prefix: property '"+o+"' exists.")}if(r[o]=u=function(e){return c(new Number(e))}(t++),f.push(r),r instanceof Array)r=c([c({[a]:c([u,"A"])}),...r]);else if(r instanceof Map)r=c([c({[a]:c([u,"M"])}),...[...r].map(e=>c(e))]);else if(r instanceof Set)r=c([c({[a]:c([u,"S"])}),...r]);else if(n){let e=n(r,a);e&&e!==r&&((r=e)[o]=u,r[a]=function(e){if("string"!=typeof e)throw new Error("Type must be a String.");return c(new String(e))}(r[a]||""))}return r})}finally{f.forEach(e=>delete e[o])}}function i(e,t){const{prefix:r,activator:n}=Object.assign({prefix:"~$£€"},t),i=r+">",o=r+"°",a=r+"þ";let s=null;const f=[];function c(e,t){let r=null;if(t instanceof Array){if(t.length>0&&null!=t[0]&&void 0!==(r=t[0][a])){switch(t.splice(0,1),r[1]){case"A":break;case"M":t=Object.assign(new Map,{v:t});break;case"S":t=Object.assign(new Set,{v:t});break;default:throw new Error("Expecting typed array to be 'A', 'M' or 'S'.")}f[r[0]]=t}}else if(null!==t){const e=t[o];void 0!==e&&(delete t[o],void 0!==(r=t[a])&&(delete t[a],n&&(t=n(t,r))&&(null===s&&(s=new Set),s.add(t))),f[e]=t)}return t}const u="string"==typeof e?JSON.parse(e,c):c(0,function e(t){if(t)if(t instanceof Array)for(let r=0;r<t.length;++r){const n=t[r];e(n),t[r]=c(0,n)}else if("object"==typeof t)for(const r in t){const n=t[r];e(n),t[r]=c(0,n)}return t}(e));function l(e,t){const r=t.length;for(let n=0;n<r;++n){const r=t[n];if(r){const o=r[i];void 0!==o&&(t[n]=e[o])}}}for(let e of f)if(null===s||!s.has(e))if(e instanceof Array)l(f,e);else if(e instanceof Map)e.v.forEach(e=>l(f,e)),e.v.forEach(t=>e.set(t[0],t[1])),delete e.v;else if(e instanceof Set)l(f,e.v),e.v.forEach(t=>e.add(t)),delete e.v;else for(const t in e){const r=e[t];if(null!==r){const n=r[i];void 0!==n&&(e[t]=f[n])}}return u}r.r(t),r.d(t,"serialize",function(){return n}),r.d(t,"deserialize",function(){return i})}])});
//# sourceMappingURL=ck-observable-domain.js.map