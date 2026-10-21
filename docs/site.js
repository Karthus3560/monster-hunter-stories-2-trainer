"use strict";
const image=document.querySelector("#gallery-image");
const caption=document.querySelector("#gallery-caption");
const full=document.querySelector("#gallery-full");
const buttons=[...document.querySelectorAll("[data-image]")];
for(const button of buttons){button.addEventListener("click",()=>{
 for(const other of buttons)other.setAttribute("aria-pressed",String(other===button));
 image.src="screenshots/"+button.dataset.image;image.alt=button.dataset.alt;
 full.href=image.src;caption.textContent=button.dataset.caption;
});}
