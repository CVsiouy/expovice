# Expovice Service Marketplace App

_Started 2026-10-17 12:17 UTC_

---

## User

Design a mobile app where users can sign in and create their accounts and fill their information and list themselves to offer services on the app. For example on the Home screen there would be cards like electrician, plumber, carpenter, AC fixer, fridge fixer, washing machine fixer, dog walking - the usual blue collar servicing jobs and then clicking on each cards would display the listing of the people with their face and their skillsets and number of service requests they have got via the app and lastly their rating from all their services which would be out of 5 and can be in decimal too. 

The users would see the listings closest to their locations only.

I am thinking of the app name as expovice . You can take inspiration from the already existing apps like swiggy, zomato and olx. 

Let me know if you have any queries

## Assistant



## Assistant

_[tool: list_files]_

_[tool: ask_user]_

I've put up a short form with questions for Expovice. It covers which device to design for, who can sign up, how people sign in, which profile fields and screens to include, how customers contact providers, the market and currency, and whether you want variations. I'll start building once you submit it. You can skip any question and I'll decide it.

## User

Answered — platform: Both; roles: One account: anyone can browse AND list themselves; signin: Phone + OTP, Google, Apple; profile_fields: Photo, Name, Service categories, Skills, Years of experience, Hourly / visit rate, Service radius, Availability hours, ID verification, Languages, Bio, Work photos; flows: Splash / onboarding, Sign in / sign up, Profile setup (multi-step), Home with category cards, Category listing, Provider detail, Request a service / booking, My requests, Provider dashboard (incoming…; listing_controls: Sort by distance, Sort by rating, Filter by price, Filter by availabi…

## Assistant

_[tool: read_file]_

_[tool: read_file]_

_[tool: copy_starter_component]_

_[tool: update_todos]_

_[tool: dc_write]_

_[tool: ready_for_verification]_

I've built the Expovice prototype in **Expovice App.dc.html** as one clickable app; a final check is still running in the background. It covers splash and onboarding, sign-in by phone code (OTP), Google or Apple, account creation, Home with category cards, the category listing, the provider page, sending a request, My requests, the 4-step "list yourself" setup, the provider dashboard and Profile.

- **Signing in:** any 10-digit number and any 4-digit code get you in. There's a "Jump to screen" list beside the phone so you can open any screen directly.
- **Listings:** a category only shows providers within 5 km of a sample address in Indiranagar, Bengaluru. Each listing has a face, skills, number of requests, a rating like 4.7/5, distance and price in ₹. You can sort by nearest or top rated, and filter by "Available today" or "Under ₹400".
- **Requests:** a customer picks a service, day and time, then sends the request in the app. On the provider side, incoming requests can be accepted or declined, and providers can pause new requests.
- **Settings panel:** you can switch between iOS and Android, and change the distance limit to 3, 5 or 8 km.
- **Things I added:** a Painter category so the grid has 8 cards. All provider names and numbers are sample data.

**Photos:** every face, work photo and the ID upload is an empty slot. Drag a photo onto one to fill it.

