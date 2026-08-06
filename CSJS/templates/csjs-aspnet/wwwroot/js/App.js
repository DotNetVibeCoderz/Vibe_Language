// Generated from App.csjs by CSJS. Do not edit.
// Gravicode Studios — led by Kang Fadhil.
import { csjsOrderBy, csjsSum } from './csjs-runtime.js';

/** Shape of the JSON the server's /api/products endpoint returns. The same record could be shared with the server project, which is the practical payoff of writing the browser code in C#: one type definition for both ends of the request. */
export class Product {
  Id = 0;
  Name = '';
  Price = 0;
  Category = '';
}

/** Browser-side application, transpiled to wwwroot/js/App.js by the project build. */
export class App {
  static Products = [];
  static _filter = 'all';

  static async Start() {
    await App.LoadProducts();
    App.WireFilters();
    App.Render();
  }

  static async LoadProducts() {
    const status = document.getElementById('status');
    if (!(status === null)) {
      status.textContent = 'Memuat produk…';
    }
    const response = await fetch('/api/products');
    if (!response.ok) {
      if (!(status === null)) {
        status.textContent = `Gagal memuat (${response.status}).`;
      }
      return;
    }
    const payload = await response.json();
    App.Products.length = 0;
    const items = payload['items'];
    for (const item of items) {
      App.Products.push(Object.assign(new Product(), { Id: item['id'], Name: item['name'], Price: item['price'], Category: item['category'] }));
    }
    if (!(status === null)) {
      status.textContent = `${App.Products.length} produk dimuat.`;
    }
  }

  static WireFilters() {
    for (const button of document.querySelectorAll('[data-filter]')) {
      const value = button.getAttribute('data-filter') ?? 'all';
      button.addEventListener('click', (_) => {
        App._filter = value;
        App.UpdateActiveFilter();
        App.Render();
      });
    }
    App.UpdateActiveFilter();
  }

  static UpdateActiveFilter() {
    for (const button of document.querySelectorAll('[data-filter]')) {
      const value = button.getAttribute('data-filter') ?? 'all';
      button.classList.toggle('active', value === App._filter);
    }
  }

  static Render() {
    const grid = document.getElementById('grid');
    if (grid === null) {
      return;
    }
    grid.innerHTML = '';
    const visible = App._filter === 'all' ? App.Products : App.Products.filter((p) => p.Category === App._filter);
    for (const product of csjsOrderBy(visible, (p) => p.Price, false)) {
      grid.appendChild(App.BuildCard(product));
    }
    const total = document.getElementById('total');
    if (!(total === null)) {
      const sum = csjsSum(visible, (p) => p.Price);
      total.textContent = `${visible.length} item — total Rp ${sum}`;
    }
  }

  static BuildCard(product) {
    const card = document.createElement('article');
    card.className = 'card';
    const name = document.createElement('h3');
    name.textContent = product.Name;
    const category = document.createElement('span');
    category.className = 'badge';
    category.textContent = product.Category;
    const price = document.createElement('p');
    price.className = 'price';
    price.textContent = `Rp ${product.Price}`;
    card.appendChild(category);
    card.appendChild(name);
    card.appendChild(price);
    return card;
  }
}
globalThis.App = App;
//# sourceMappingURL=App.js.map
