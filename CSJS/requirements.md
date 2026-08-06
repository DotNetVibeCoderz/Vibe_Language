name: CSJS

deskripsi:
sebuah engine transpiler baru yang mirip TypeScript, tapi khusus untuk C# → JavaScript. Supaya lengkap, berikut daftar fitur yang bisa menjadi fondasi ekosistemnya:

---

 🚀 Core Engine Features
- C# to JS Transpilation  
  Mendukung subset besar dari sintaks C# (class, interface, generics, async/await) yang dikonversi ke JavaScript modern (ESNext).
- Type Safety Layer  
  Menjaga strong typing ala C#, dengan mapping ke TypeScript type definitions agar interoperabilitas tetap terjaga.
- Interop dengan JS/TS  
  Bisa memanggil library JavaScript langsung dari C#, dengan binding otomatis.
- Optimized Output  
  Menghasilkan kode JS yang clean, readable, dan mendukung tree-shaking.

---

 📦 Project Templates (.NET)
- Blazor Hybrid Template  
  Project starter yang menggabungkan Blazor Server dengan transpiler engine untuk frontend JS.
- ASP.NET + JS Template  
  Pipeline build otomatis: C# transpile → JS → bundle → serve.
- Console-to-Web Template  
  Membuat project console C# yang langsung bisa dijalankan di browser via transpiler.

---

 🛠️ VSCode Extension
- Syntax Highlighting untuk file `.csjs` (C# for JS).  
- IntelliSense dengan type inference dari transpiler.  
- Debugging Bridge: step-through debugging dari C# source ke JS output.  
- Build Tasks: integrasi langsung dengan `dotnet build`.

---

 🌐 Sample Code Web (C# + HTML)
- Hello World Web: C# class transpile ke JS, dipanggil dari HTML.  
- DOM Manipulation: contoh `document.querySelector` via C#.  
- Event Handling: klik tombol HTML → trigger method C#.  
- Three.js Integration: render 3D scene dengan C# transpile ke JS.

---

 🔧 Ecosystem & Tools
- NuGet Package untuk integrasi di project .NET.  
- CLI Tool: `dotnet cs2js build` untuk compile cepat.  
- Playground Web: editor online untuk mencoba transpiler langsung di browser.  
- Interop Library: binding otomatis ke popular JS frameworks (React, Vue, Angular).  
- Testing Framework: unit test di C#, dijalankan di JS runtime.

---

 🔧 CSJS Code Generator

# CSJSGen - aplikasi desktop Avalonia + Blazor dengan Semantic Kernel Integration untuk AI-driven code generation. Desain UI dan UX yang modern, fresh, fun dibantu skill frontend-design dengan dukungan dark theme/light, micro-interaction. 

# Fitur Code Assistant:
  - Nama 'Jack The Code Bender'
  - Bentuknya adalah Chat Panel di sebelah kanan code editor dengan tampilan yang keren, multi session (create/delete), reset session, bisa attach gambar (diupload lalu url-nya di jadikan image content) dan dokumen (di upload dan disertakan linknya ke text message).
  - Chat Panel bisa di show/hide
  - System Prompt (persona), temperature, model dan setting lainnya di simpan di app.config
  - Menggunakan Semantic Kernel Library dengan dukungan model: Open AI, Anthropic, Gemini, Ollama (bisa pilih)
  - Tambahkan beberapa common functions (kernel functions) yang diperlukan termasuk query ke tavily (search internet), scrap page url, baca file dari url, cek tanggal, Waktu, math calculation, dan beberapa function yang diperlukan lainnya
  - Tambahkan kernel functions untuk mengenerate, run, compile code aplikasi dengan CSJS
  - Bisa render chat thread dengan mark down dengan baik ke html (baik table, media (image, video, audio), code, dan lainnya dengan baik)
  - Berikan contoh-contoh template prompt untuk generate aplikasi yang menarik (bikinkan yang banyak)

# Fitur Code Editor
- panel di kanan (property) dan kiri (project explorer), tengah (viewer/code editor)
- panel kanan dan kiri bisa di show/hide
- tambahkan fitur standard editor code: cut/copy/paste, go to line, search, replace, code/xml formatter
- fitur run/compile code
- menu new project, open project, save/save as juga berlaku untuk code (.csjs), tambahkan juga close project.

---

 📊 Roadmap Tambahan

- WebAssembly Output sebagai opsi selain JS.  
- Cross-platform Templates untuk mobile (MAUI) dan IoT.  

---

Notes:
- dibuat dengan .NET 10 
- Tambahkan readme.md (English dan Bahasa Indonesia), sertakan screenshot
- Tambahkan dokumentasi lengkap di folder docs (sertakan screenshot)
- Tambahkan info di dokumentasi dan app: dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
- Plan.md untuk roadmap pengembangan, Progress.md untuk tracking development

---
Summary:
Ini bisa menjadi “TypeScript versi .NET” — menjembatani developer C# dengan ekosistem web modern.  