"use strict";var SkLoginWidget=(()=>{var q=Object.defineProperty;var ke=Object.getOwnPropertyDescriptor;var he=Object.getOwnPropertyNames;var fe=Object.prototype.hasOwnProperty;var me=(n,s)=>{for(var t in s)q(n,t,{get:s[t],enumerable:!0})},ue=(n,s,t,g)=>{if(s&&typeof s=="object"||typeof s=="function")for(let d of he(s))!fe.call(n,d)&&d!==t&&q(n,d,{get:()=>s[d],enumerable:!(g=ke(s,d))||g.enumerable});return n};var we=n=>ue(q({},"__esModule",{value:!0}),n);var Ce={};me(Ce,{SK_LOGO:()=>L,mountSkLogin:()=>De,texts:()=>te});var L="data:image/png;base64,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";var xe=`
.skl {
  --skl-bg: #f1f1f3;
  --skl-item: #fcfcff;
  --skl-separator: #e4e4e7;
  --skl-secondary: #636369;
  --skl-text: #010102;
  --skl-primary: #326afb;
  --skl-primary-soft: rgba(50, 106, 251, 0.12);
  --skl-danger: #de3b3b;
  --skl-surface-high: #dfe3e7;
  --skl-font: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
}
@media (prefers-color-scheme: dark) {
  .skl:not([data-theme="light"]) {
    --skl-bg: #010102;
    --skl-item: #17171a;
    --skl-separator: #3a3a3d;
    --skl-secondary: #99999e;
    --skl-text: #fafaff;
    --skl-surface-high: #2c2c30;
  }
}
.skl[data-theme="dark"] {
  --skl-bg: #010102;
  --skl-item: #17171a;
  --skl-separator: #3a3a3d;
  --skl-secondary: #99999e;
  --skl-text: #fafaff;
  --skl-surface-high: #2c2c30;
}
dialog.skl {
  padding: 0;
  border: 0;
  background: var(--skl-item);
  color: var(--skl-text);
  font: 16px/1.5 var(--skl-font);
  text-align: center;
  width: min(480px, calc(100% - 32px));
  max-height: 85vh;
  border-radius: 14px;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.3);
  overflow: auto;
  box-sizing: border-box;
}
dialog.skl * { box-sizing: border-box; }
dialog.skl[open] { animation: skl-in 220ms cubic-bezier(0.215, 0.61, 0.355, 1); }
dialog.skl::backdrop { background: rgba(0, 0, 0, 0.54); }
@keyframes skl-in { from { opacity: 0; transform: scale(0.95); } to { opacity: 1; transform: none; } }
.skl-inner { padding: 12px 16px 16px; position: relative; }
.skl-bar { display: grid; grid-template-columns: 44px 1fr 44px; align-items: center; gap: 8px; min-height: 44px; }
.skl.info .skl-bar { display: none; }
.skl-close {
  width: 44px; height: 44px; border-radius: 50%; border: 0; padding: 0;
  display: inline-flex; align-items: center; justify-content: center;
  background: var(--skl-surface-high); color: var(--skl-text); cursor: pointer;
}
.skl-close svg { width: 22px; height: 22px; }
.skl-title { margin: 0; font-size: 1.25rem; font-weight: 600; line-height: 1.3; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.skl-spacer { width: 44px; height: 44px; display: inline-flex; align-items: center; justify-content: center; }
.skl-ttl { font-size: 0.9rem; font-weight: 600; font-variant-numeric: tabular-nums; color: var(--skl-secondary); }
.skl-ttl.soon { color: var(--skl-danger); }
.skl-body { padding-top: 12px; }
.skl-secondary { color: var(--skl-secondary); }
.skl-hint { margin: 4px 0 0; font-size: 0.9rem; line-height: 1.45; text-wrap: balance; }
.skl-hint a { color: var(--skl-primary); text-decoration: none; white-space: nowrap; }
.skl-hint-icon { display: inline-block; width: 18px; height: 18px; margin: 0 0.15em; vertical-align: -0.25em; color: var(--skl-text); }
.skl-qr {
  position: relative; display: block; width: 232px; height: 232px; margin: 18px auto 0;
  border-radius: 12px; background: #fff; padding: 10px; text-decoration: none;
}
.skl-qr svg { width: 100%; height: 100%; display: block; }
.skl-qr-logo {
  position: absolute; top: 50%; left: 50%; width: 44px; height: 44px; padding: 5px; box-sizing: content-box;
  transform: translate(-50%, -50%); background: #fff; border-radius: 50%;
}
.skl-qr.loading { background: var(--skl-surface-high); }
.skl-qr.loading > span:first-child, .skl-qr.loading .skl-qr-logo { visibility: hidden; }
.skl-qr-spinner { display: none; position: absolute; top: 50%; left: 50%; translate: -50% -50%; }
.skl-qr.loading .skl-qr-spinner {
  display: block; width: 32px; height: 32px; border-radius: 50%;
  border: 3px solid color-mix(in srgb, var(--skl-secondary) 35%, transparent); border-top-color: var(--skl-primary);
  animation: skl-spin 0.8s linear infinite;
}
@keyframes skl-spin { to { transform: rotate(360deg); } }
.skl-btn {
  display: inline-flex; align-items: center; justify-content: center; gap: 8px;
  padding: 11px 20px; border-radius: 12px; border: 0; font: inherit; font-weight: 600; cursor: pointer;
  text-decoration: none; background: var(--skl-primary); color: #fff; min-width: 232px;
}
.skl-btn:hover { filter: brightness(1.06); }
.skl-qr + .skl-btn { margin-top: 20px; }
.skl-noapp { margin: 12px 0 0; font-size: 0.9rem; }
.skl-noapp a { color: var(--skl-primary); }
.skl-logo { display: block; position: relative; width: 56px; height: 56px; margin: 16px auto 20px; border-radius: 50%; }
.skl-logo img { display: block; width: 100%; height: 100%; border-radius: 50%; }
.skl-logo::after {
  content: ''; position: absolute; inset: -6px; border-radius: 50%;
  border: 2px solid var(--skl-separator); border-top-color: var(--skl-primary); animation: skl-spin 1s linear infinite;
}
.skl-lead { margin: 4px 0 18px; }
.skl-lead:has(+ .skl-lead:not(.skl-hidden)) { margin-bottom: 8px; }
.skl-link-btn { font: inherit; padding: 0; border: 0; background: none; color: var(--skl-primary); cursor: pointer; }
.skl-code-form { width: 100%; max-width: 360px; margin: 0 auto 4px; display: flex; flex-direction: column; align-items: stretch; gap: 12px; }
.skl-code-label { margin: 0 0 -4px; font-size: 0.9rem; }
.skl-field {
  font: inherit; padding: 10px 12px; border-radius: 10px; border: 1px solid var(--skl-separator);
  background: var(--skl-bg); color: var(--skl-text); width: 100%; letter-spacing: 0.15em; text-align: center;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}
.skl-field::placeholder { letter-spacing: normal; font-family: var(--skl-font); }
.skl-field.error { border-color: var(--skl-danger); }
.skl-field.error::placeholder { color: var(--skl-danger); }
.skl-info { display: flex; flex-direction: column; align-items: stretch; padding-top: 8px; }
.skl-info-icon { display: block; width: 40px; height: 40px; margin: 0 auto; color: var(--skl-primary); }
.skl-info-icon.danger { color: var(--skl-danger); }
.skl-info-text { margin: 16px 0 24px; font-size: 1rem; line-height: 1.45; }
.skl-btn.outline { width: 100%; min-height: 48px; border-radius: 999px; border: 1px solid var(--skl-primary); background: transparent; color: var(--skl-primary); min-width: 0; }
.skl-expired { position: absolute; inset: 0; z-index: 2; display: flex; align-items: flex-end; background: rgba(0, 0, 0, 0.45); }
.skl-expired-card {
  width: 100%; padding: 18px 16px 16px; background: var(--skl-item); border-radius: 16px 16px 0 0;
  box-shadow: 0 -6px 24px rgba(0, 0, 0, 0.25); display: flex; flex-direction: column; align-items: center;
  animation: skl-up 0.25s ease-out;
}
.skl-expired-icon { display: block; width: 44px; height: 44px; margin: 10px auto 6px; color: var(--skl-primary); }
.skl-expired-title { margin: 8px 0 18px; font-weight: 600; font-size: 1.1rem; }
@keyframes skl-up { from { transform: translateY(100%); } to { transform: none; } }
.skl-hidden { display: none !important; }
@media (max-width: 520px) {
  dialog.skl { width: 100%; max-width: none; margin: auto 0 0; border-radius: 16px 16px 0 0; max-height: 92vh; }
  .skl-inner { padding-bottom: calc(16px + env(safe-area-inset-bottom)); }
}
`,_=!1;function ee(n){if(_||n.getElementById("skl-styles"))return;let s=n.createElement("style");s.id="skl-styles",s.textContent=xe,n.head.appendChild(s),_=!0}var te={ru:{title:"\u0412\u0445\u043E\u0434",close:"\u0417\u0430\u043A\u0440\u044B\u0442\u044C",scan1:"\u041E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 \u043F\u0440\u0438\u043B\u043E\u0436\u0435\u043D\u0438\u0435 ",scanLink:"Secret Keeper",scan2:" \u043D\u0430 \u0434\u0440\u0443\u0433\u043E\u043C \u0443\u0441\u0442\u0440\u043E\u0439\u0441\u0442\u0432\u0435, \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u0437\u043D\u0430\u0447\u043E\u043A ",scan3:" \u0432 \u0432\u0435\u0440\u0445\u043D\u0435\u0439 \u043F\u0430\u043D\u0435\u043B\u0438 \u0438 \u043D\u0430\u0432\u0435\u0434\u0438\u0442\u0435 \u043A\u0430\u043C\u0435\u0440\u0443 \u043D\u0430 QR-\u043A\u043E\u0434:",open:"\u0412\u043E\u0439\u0434\u0438\u0442\u0435 \u0447\u0435\u0440\u0435\u0437 \u043F\u0440\u0438\u043B\u043E\u0436\u0435\u043D\u0438\u0435",noapp1:"\u041F\u0440\u0438\u043B\u043E\u0436\u0435\u043D\u0438\u0435 \u043D\u0435 \u043E\u0442\u043A\u0440\u044B\u043B\u043E\u0441\u044C? \u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u0435 ",noappLink:"Secret Keeper",lead1:"\u041E\u0436\u0438\u0434\u0430\u043D\u0438\u0435 \u043F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043D\u0438\u044F \u0432 Secret Keeper\u2026",lead2:"\u0415\u0441\u043B\u0438 \u043F\u0440\u0438\u043B\u043E\u0436\u0435\u043D\u0438\u0435 \u043F\u043E\u043A\u0430\u0437\u0430\u043B\u043E \u043A\u043E\u0434, \u0442\u043E ",leadLink:"\u0432\u0432\u0435\u0434\u0438\u0442\u0435 \u0435\u0433\u043E",codeLabel:"\u041A\u043E\u0434 \u0438\u0437 \u043F\u0440\u0438\u043B\u043E\u0436\u0435\u043D\u0438\u044F",submit:"\u0412\u043E\u0439\u0442\u0438",wrong:"\u041A\u043E\u0434 \u043D\u0435 \u043F\u043E\u0434\u043E\u0448\u0451\u043B",denied:"\u0412\u0445\u043E\u0434 \u043F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0451\u043D, \u0434\u043E\u0441\u0442\u0443\u043F \u043F\u043E\u043A\u0430 \u043D\u0435 \u043E\u0442\u043A\u0440\u044B\u0442. \u0415\u0441\u043B\u0438 \u0432\u0430\u0441 \u0437\u0434\u0435\u0441\u044C \u0436\u0434\u0443\u0442, \u0441\u043B\u0435\u0434\u0443\u044E\u0449\u0438\u0439 \u0432\u0445\u043E\u0434 \u043F\u0440\u043E\u0439\u0434\u0451\u0442.",cancelled:"\u0412\u044B \u043E\u0442\u043A\u043B\u043E\u043D\u0438\u043B\u0438 \u044D\u0442\u043E\u0442 \u0437\u0430\u043F\u0440\u043E\u0441 \u0432 Secret Keeper.",timeout:"\u0412\u0440\u0435\u043C\u044F \u043E\u0436\u0438\u0434\u0430\u043D\u0438\u044F \u0438\u0441\u0442\u0435\u043A\u043B\u043E: \u043F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043D\u0438\u0435 \u0438\u0437 Secret Keeper \u043D\u0435 \u043F\u0440\u0438\u0448\u043B\u043E. \u041F\u043E\u043F\u0440\u043E\u0431\u0443\u0439\u0442\u0435 \u0432\u043E\u0439\u0442\u0438 \u0435\u0449\u0451 \u0440\u0430\u0437.",expired:"QR-\u043A\u043E\u0434 \u0443\u0441\u0442\u0430\u0440\u0435\u043B",offline:"\u041D\u0435\u0442 \u0441\u0432\u044F\u0437\u0438 \u0441 \u0441\u0430\u0439\u0442\u043E\u043C",refresh:"\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C"},en:{title:"Sign in",close:"Close",scan1:"Open the ",scanLink:"Secret Keeper",scan2:" app on another device, tap the ",scan3:" icon in the top bar and point the camera at the QR code:",open:"Sign in with the app",noapp1:"The app did not open? Install ",noappLink:"Secret Keeper",lead1:"Waiting for confirmation in Secret Keeper\u2026",lead2:"If the app showed you a code, ",leadLink:"enter it",codeLabel:"Code from the app",submit:"Sign in",wrong:"That code did not match",denied:"Sign-in confirmed, but access is not open yet. If you are expected here, your next sign-in will go through.",cancelled:"You declined this request in Secret Keeper.",timeout:"Time ran out: no confirmation came from Secret Keeper. Try signing in again.",expired:"The QR code has expired",offline:"Could not reach the site",refresh:"Refresh"}},be=2e3,Be=1e4,ve='<svg class="skl-hint-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M3 11h8V3H3v8zm2-6h4v4H5V5zM3 21h8v-8H3v8zm2-6h4v4H5v-4zm8-12v8h8V3h-8zm6 6h-4V5h4v4zm-6 4h2v2h-2v-2zm2 2h2v2h-2v-2zm-2 2h2v2h-2v-2zm4 0h2v2h-2v-2zm2 2h2v2h-2v-2zm-4 0h2v2h-2v-2zm2-6h2v2h-2v-2zm2 2h2v2h-2v-2z"/></svg>',Y=(n,s="")=>`<svg class="skl-info-icon ${s}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${n}</svg>`;function r(n){return n.replace(/[&<>"]/g,s=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"})[s])}function De(n){let s=document;ee(s);let t={...te[n.lang??"ru"],...n.texts},g=(n.apiBase??"/api/sk").replace(/\/$/,""),d=n.logoUrl??L,F=n.skSiteUrl??"https://secretkeeper.net",ne=n.pollMs??2e3,u=`skl${Math.random().toString(36).slice(2,8)}`,i=s.createElement("dialog");i.className="skl",n.theme&&(i.dataset.theme=n.theme),i.setAttribute("aria-labelledby",`${u}-title`),i.innerHTML=`
<div class="skl-inner">
  <div class="skl-bar">
    <button class="skl-close" type="button" aria-label="${r(t.close)}" title="${r(t.close)}" data-r="close">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>
    </button>
    <h2 class="skl-title" id="${u}-title">${r(t.title)}</h2>
    <span class="skl-spacer"><span class="skl-ttl skl-hidden" data-r="ttl"></span></span>
  </div>
  <div class="skl-body">
    <section data-view="scan">
      <p class="skl-secondary skl-hint">${r(t.scan1)}<a href="${r(F)}" target="_blank" rel="noopener">${r(t.scanLink)}</a>${r(t.scan2)}${ve}${r(t.scan3)}</p>
      <a class="skl-qr loading" href="#" data-r="qr-link" aria-label="QR">
        <span data-r="qr"></span>
        <img class="skl-qr-logo" src="${r(d)}" alt="" width="44" height="44">
        <span class="skl-qr-spinner" aria-hidden="true"></span>
      </a>
      <a class="skl-btn" href="#" data-r="open">${r(t.open)}</a>
      <p class="skl-secondary skl-noapp skl-hidden" data-r="noapp">${r(t.noapp1)}<a href="${r(F)}" target="_blank" rel="noopener">${r(t.noappLink)}</a></p>
    </section>
    <section data-view="challenged" class="skl-hidden">
      <span class="skl-logo"><img src="${r(d)}" alt="Secret Keeper" width="56" height="56"></span>
      <p class="skl-secondary skl-lead">${r(t.lead1)}</p>
      <p class="skl-secondary skl-lead skl-hidden" data-r="code-hint">${r(t.lead2)}<button class="skl-link-btn" type="button" data-r="code-open">${r(t.leadLink)}</button></p>
      <div class="skl-code-form skl-hidden" data-r="code-form">
        <label class="skl-secondary skl-code-label" for="${u}-code">${r(t.codeLabel)}</label>
        <input class="skl-field" id="${u}-code" data-r="code" inputmode="numeric" pattern="[0-9]*" maxlength="12" autocomplete="one-time-code" enterkeyhint="go">
        <button class="skl-btn" type="button" data-r="code-submit">${r(t.submit)}</button>
      </div>
    </section>
    <section data-view="denied" class="skl-hidden skl-info">
      ${Y('<rect x="4" y="10.5" width="16" height="10" rx="2.5"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/><circle cx="12" cy="15.5" r="1.2" fill="currentColor" stroke="none"/>',"danger")}
      <p class="skl-info-text" data-r="denied-text">${r(t.denied)}</p>
      <button class="skl-btn outline" type="button" data-r="close">${r(t.close)}</button>
    </section>
    <section data-view="cancelled" class="skl-hidden skl-info">
      ${Y('<circle cx="12" cy="12" r="8.5"/><path d="M9 9l6 6M15 9l-6 6"/>')}
      <p class="skl-info-text">${r(t.cancelled)}</p>
      <button class="skl-btn outline" type="button" data-r="close">${r(t.close)}</button>
    </section>
    <section data-view="timeout" class="skl-hidden skl-info">
      ${Y('<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>')}
      <p class="skl-info-text">${r(t.timeout)}</p>
      <button class="skl-btn outline" type="button" data-r="close">${r(t.close)}</button>
    </section>
  </div>
  <div class="skl-expired skl-hidden" role="alertdialog" data-r="expired">
    <div class="skl-expired-card">
      <svg class="skl-expired-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 12a8 8 0 1 1-2.34-5.66"/><path d="M20 4v5h-5"/></svg>
      <p class="skl-expired-title" data-r="expired-title"></p>
      <button class="skl-btn" type="button" data-r="refresh">${r(t.refresh)}</button>
    </div>
  </div>
</div>`,s.body.appendChild(i);let l=e=>i.querySelector(`[data-r="${e}"]`),se=Array.from(i.querySelectorAll("[data-view]")),ie=l("qr"),h=l("qr-link"),w=l("open"),I=l("noapp"),E=l("ttl"),A=l("code"),R=l("code-form"),N=l("code-hint"),re=l("denied-text"),S=l("expired"),oe=l("expired-title"),M="",p="scan",f,k,x,b=0,B=0,T=0,v=!1,H=(e={})=>({cache:"no-store",credentials:n.credentials??"same-origin",...e,headers:{...n.headers,...e.headers}}),P=()=>h.classList.contains("loading"),U=()=>!S.classList.contains("skl-hidden"),ae=()=>Date.now()-b<=B,le=()=>Date.now()-b>=B/2,O=()=>{let e=p!=="scan"&&p!=="challenged";E.classList.toggle("skl-hidden",e||P()||U())},X=()=>{let e=Math.max(0,B-(Date.now()-b)),o=Math.ceil(e/1e3);E.textContent=`${Math.floor(o/60)}:${String(o%60).padStart(2,"0")}`,E.classList.toggle("soon",e<=Be)},j=()=>{b=Date.now(),k&&window.clearInterval(k),X(),k=window.setInterval(X,1e3)},D=e=>{p=e;for(let a of se)a.classList.toggle("skl-hidden",a.dataset.view!==e);let o=e==="denied"||e==="cancelled"||e==="timeout";i.classList.toggle("info",o),O(),o&&s.activeElement?.blur()},C=()=>{x&&window.clearTimeout(x),x=void 0,window.removeEventListener("blur",y),s.removeEventListener("visibilitychange",y)},y=()=>{(s.visibilityState==="hidden"||!s.hasFocus())&&C()},Ae=()=>{C(),I.classList.add("skl-hidden"),window.addEventListener("blur",y),s.addEventListener("visibilitychange",y),x=window.setTimeout(()=>{C(),I.classList.remove("skl-hidden")},be)},c=()=>{f&&window.clearInterval(f),f=void 0,k&&window.clearInterval(k),k=void 0,C()},G=e=>{i.open&&i.close(),D(e),i.showModal()},m=(e="expired")=>{if(c(),e==="expired"&&p==="challenged")return G("timeout");oe.textContent=e==="offline"?t.offline:t.expired,S.classList.remove("skl-hidden"),O(),l("refresh").focus()},K=e=>{c(),i.close(),n.onSuccess(e)},V=(e,o)=>{c(),re.textContent=o||t.denied,D("denied"),n.onDenied?.(e,o)},de=()=>{c(),G("cancelled"),n.onCancelled?.()},ce=async()=>{if(!ae())return m();try{let o=await(await fetch(`${g}/status?sid=${encodeURIComponent(M)}`,H())).json();if(!f)return;let{state:a,reason:z,...pe}=o;if(a==="authenticated")return K(pe);if(a==="denied")return V(z);if(a==="expired")return m();if(a==="cancelled")return de();a==="challenged"&&(p!=="challenged"?(j(),N.classList.add("skl-hidden"),D("challenged")):le()&&N.classList.remove("skl-hidden"))}catch{}},W=()=>{A.placeholder="",A.classList.remove("error")},J=async()=>{c();let e=++T;v=!1,A.value="",W(),R.classList.add("skl-hidden"),S.classList.add("skl-hidden"),I.classList.add("skl-hidden"),h.classList.add("loading"),D("scan");try{let o=await fetch(`${g}/init`,H({method:"POST"}));if(!o.ok)throw new Error(String(o.status));let a=await o.json();if(e!==T||!i.open)return;M=a.sid,B=a.ttlMs,ie.innerHTML=a.qrSvg??"",h.href=a.schemeUrl,w.href=a.schemeUrl,h.classList.remove("loading"),j(),O(),f=window.setInterval(ce,ne),v&&(v=!1,w.click())}catch{e===T&&i.open&&m("offline")}},Z=async()=>{let e=A.value.trim();if(!e)return A.focus();let o;try{o=await fetch(`${g}/code`,H({method:"POST",headers:{"content-type":"application/json"},body:JSON.stringify({sid:M,code:e})}))}catch{return m("offline")}if(o.ok){let{ok:a,...z}=await o.json();return K(z)}if(o.status===403){let a=await o.json().catch(()=>({}));return V(a.reason,a.message)}if(o.status===400){A.value="",A.placeholder=t.wrong,A.classList.add("error"),A.focus();return}m()},ge=()=>{i.open||(i.showModal(),J())},Q=()=>i.close();w.addEventListener("click",e=>{if(P()){e.preventDefault(),v=!0;return}Ae()}),h.addEventListener("click",e=>{P()&&e.preventDefault()});for(let e of i.querySelectorAll('[data-r="close"]'))e.addEventListener("click",Q);l("code-open").addEventListener("click",()=>{R.classList.remove("skl-hidden"),A.focus()}),l("code-submit").addEventListener("click",Z),l("refresh").addEventListener("click",J),A.addEventListener("input",W),A.addEventListener("keydown",e=>{e.key==="Enter"&&(e.preventDefault(),Z())}),i.addEventListener("click",e=>{e.target===i&&Q()}),i.addEventListener("close",()=>{c(),s.activeElement?.blur(),n.onClose?.()});let $=e=>{if(i.open){if(e.key==="Escape"){e.preventDefault(),Q();return}if(e.key==="Enter"&&p==="scan"&&!U()){let o=e.target;if(i.contains(o)&&o.closest("button, input, a"))return;e.preventDefault(),w.click()}}};return s.addEventListener("keydown",$),{open:ge,close:Q,destroy(){c(),s.removeEventListener("keydown",$),i.open&&i.close(),i.remove()},element:i}}return we(Ce);})();
//# sourceMappingURL=sk-login-widget.global.js.map