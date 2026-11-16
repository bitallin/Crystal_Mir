(function () {
  function esc(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function clone(value) {
    return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
  }

  function getPath(source, path) {
    return String(path).split(".").reduce((value, key) => {
      if (value === null || value === undefined || typeof value !== "object") return undefined;
      return value[key];
    }, source);
  }

  function setPath(target, path, value) {
    const parts = String(path).split(".");
    let cursor = target;
    for (let index = 0; index < parts.length - 1; index++) {
      const part = parts[index];
      if (!cursor[part] || typeof cursor[part] !== "object" || Array.isArray(cursor[part])) {
        cursor[part] = {};
      }
      cursor = cursor[part];
    }
    cursor[parts[parts.length - 1]] = value;
  }

  function fields(schema) {
    return (schema.sections || []).flatMap(section => section.fields || []);
  }

  function defaultData(schema) {
    const data = {};
    fields(schema).forEach(field => {
      if (field.kind === "object" && Array.isArray(field.fields)) {
        const object = {};
        setPath(data, field.path, object);
        field.fields.forEach(child => {
          setPath(data, child.path, clone(child.defaultValue));
        });
        return;
      }
      setPath(data, field.path, clone(field.defaultValue));
    });
    return data;
  }

  function renderForm(schema, data, options = {}) {
    const errors = options.errors || {};
    return `<form class="js-content-editor editor-form" data-schema-path="${esc(options.schemaPath || "content")}">
      ${(schema.sections || []).map(section => `
        <section class="editor-section">
          <div class="editor-section-head"><div><strong>${esc(section.label)}</strong><span>${esc(section.description || "")}</span></div><span class="tag">${(section.fields || []).length} 项</span></div>
          <div class="editor-section-body">${(section.fields || []).map(field => renderField(field, data, errors, options)).join("")}</div>
        </section>`).join("")}
    </form>`;
  }

  function renderField(field, data, errors, options) {
    if (field.kind === "object" && Array.isArray(field.fields)) {
      return `<div class="editor-object"><div class="editor-object-head"><strong>${esc(field.label)}</strong><span>${field.fields.length} 个字段</span></div><div class="editor-object-grid">${field.fields.map(child => renderField(child, data, errors, options)).join("")}</div></div>`;
    }
    if (field.kind === "stat-map") {
      return renderStats(field, data, errors);
    }

    const value = getPath(data, field.path);
    const error = (errors[field.path] || [])[0];
    const disabled = options.readOnly || field.readOnly ? " disabled" : "";
    const required = field.required ? `<span class="editor-required">必填</span>` : "";
    let control = "";
    if (field.kind === "boolean") {
      control = `<label class="editor-check"><input class="checkbox" type="checkbox" data-edit-path="${esc(field.path)}"${value ? " checked" : ""}${disabled}><span data-boolean-label>${value ? "已启用" : "未启用"}</span></label>`;
    } else if (field.kind === "enum") {
      control = `<select data-edit-path="${esc(field.path)}"${disabled}>${(field.options || []).map(option => `<option value="${esc(option.value)}"${String(option.value) === String(value) ? " selected" : ""}>${esc(option.label)} · ${esc(option.value)}</option>`).join("")}</select>`;
    } else if (field.kind === "flags") {
      const selected = new Set(String(value || "None").split(",").map(item => item.trim()).filter(Boolean));
      control = `<div class="editor-flags">${(field.options || []).map(option => `<label class="editor-check"><input class="checkbox" type="checkbox" data-edit-flag-path="${esc(field.path)}" value="${esc(option.value)}"${selected.has(option.value) ? " checked" : ""}${disabled}><span>${esc(option.label)}</span></label>`).join("")}</div>`;
    } else if (field.kind === "integer") {
      const min = field.min === null || field.min === undefined ? "" : ` min="${esc(field.min)}"`;
      const max = field.max === null || field.max === undefined ? "" : ` max="${esc(field.max)}"`;
      const placeholder = field.readOnly && field.path === "Index" ? ` placeholder="自动分配"` : "";
      control = `<input type="number" step="1" value="${value === null || value === undefined ? "" : esc(value)}" data-edit-path="${esc(field.path)}"${min}${max}${placeholder}${disabled}>`;
    } else {
      control = `<input type="text" value="${esc(value ?? "")}" data-edit-path="${esc(field.path)}"${disabled}>`;
    }

    return `<div class="field editor-field${error ? " has-error" : ""}" data-field-path="${esc(field.path)}"><label>${esc(field.label)}${required}</label>${control}<div class="editor-hint mono">${esc(field.path)}</div>${error ? `<div class="editor-error">${esc(error)}</div>` : ""}</div>`;
  }

  function renderStats(field, data, errors) {
    const values = getPath(data, field.path)?.Values || {};
    const options = field.options || [];
    const rows = Object.entries(values)
      .map(([key, value]) => statRow(options, key, value, (errors[`${field.path}.Values.${key}`] || [])[0]))
      .join("");
    const error = (errors[`${field.path}.Values`] || [])[0];
    return `<div class="editor-object editor-stats" data-stat-map="${esc(field.path)}">
      <div class="editor-object-head"><strong>${esc(field.label)}</strong><span>只保存非零属性</span></div>
      <div class="editor-stat-rows">${rows}</div>
      <template class="editor-stat-template">${statRow(options, options[0]?.value, 0)}</template>
      <button class="btn btn-ghost btn-sm" type="button" data-action="editor-add-stat">+ 添加属性</button>
      ${error ? `<div class="editor-error">${esc(error)}</div>` : ""}
    </div>`;
  }

  function statRow(options, selectedValue, value, error) {
    return `<div class="editor-stat-row${error ? " has-error" : ""}"><select data-stat-key>${options.map(option => `<option value="${esc(option.value)}"${option.value === selectedValue ? " selected" : ""}>${esc(option.label)} · ${esc(option.value)}</option>`).join("")}</select><input type="number" step="1" data-stat-value value="${value === undefined || value === null ? 0 : esc(value)}"><button class="icon-btn" type="button" data-action="editor-remove-stat" title="移除属性">×</button>${error ? `<div class="editor-error">${esc(error)}</div>` : ""}</div>`;
  }

  function addStatRow(container) {
    if (!container) return;
    const rows = container.querySelector(".editor-stat-rows");
    const template = container.querySelector("template.editor-stat-template");
    if (!rows || !template?.content?.firstElementChild) return;
    const selected = new Set(Array.from(container.querySelectorAll("[data-stat-key]")).map(select => select.value));
    const row = template.content.firstElementChild.cloneNode(true);
    const select = row.querySelector("[data-stat-key]");
    const next = Array.from(select.options).find(option => !selected.has(option.value)) || select.options[0];
    if (next) select.value = next.value;
    rows.appendChild(row);
  }

  function readForm(form, schema, original) {
    const data = clone(original || {});
    fields(schema).forEach(field => {
      if (field.kind === "object" && Array.isArray(field.fields)) {
        field.fields.forEach(child => readField(form, child, data));
      } else if (field.kind === "stat-map") {
        const container = form.querySelector(`[data-stat-map="${cssEscape(field.path)}"]`);
        const values = {};
        container?.querySelectorAll(".editor-stat-row").forEach(row => {
          const key = row.querySelector("[data-stat-key]")?.value;
          const number = Number(row.querySelector("[data-stat-value]")?.value);
          if (key && Number.isFinite(number) && number !== 0) values[key] = Math.trunc(number);
        });
        setPath(data, field.path, { Values: values });
      } else {
        readField(form, field, data);
      }
    });
    return data;
  }

  function readField(form, field, data) {
    if (field.readOnly) return;
    if (field.kind === "flags") {
      const checked = Array.from(form.querySelectorAll(`[data-edit-flag-path="${cssEscape(field.path)}"]:checked`))
        .map(input => input.value)
        .filter(value => value !== "None");
      setPath(data, field.path, checked.length ? checked.join(", ") : "None");
      return;
    }

    const control = form.querySelector(`[data-edit-path="${cssEscape(field.path)}"]`);
    if (!control) return;
    if (field.kind === "boolean") {
      setPath(data, field.path, Boolean(control.checked));
    } else if (field.kind === "integer") {
      const number = Number(control.value);
      setPath(data, field.path, Number.isFinite(number) ? Math.trunc(number) : 0);
    } else {
      setPath(data, field.path, control.value);
    }
  }

  function diff(before, after, schema) {
    const left = before || {};
    const right = after || {};
    const rows = [];
    const fieldList = fields(schema);
    fieldList.forEach(field => {
      if (field.kind === "stat-map") {
        const beforeStats = getPath(left, field.path)?.Values || {};
        const afterStats = getPath(right, field.path)?.Values || {};
        const keys = Array.from(new Set([...Object.keys(beforeStats), ...Object.keys(afterStats)])).sort();
        keys.forEach(key => {
          if (!same(beforeStats[key], afterStats[key])) {
            const option = (field.options || []).find(item => item.value === key);
            rows.push({ path: `${field.path}.Values.${key}`, label: `属性 · ${option?.label || key}`, before: beforeStats[key], after: afterStats[key] });
          }
        });
        return;
      }
      if (field.kind === "object" && Array.isArray(field.fields)) {
        field.fields.forEach(child => pushDiff(rows, child, getPath(left, child.path), getPath(right, child.path)));
        return;
      }
      pushDiff(rows, field, getPath(left, field.path), getPath(right, field.path));
    });

    const known = new Set(fieldList.map(field => String(field.path).split(".")[0]));
    const unknown = Array.from(new Set([...Object.keys(left), ...Object.keys(right)]))
      .filter(key => !known.has(key));
    unknown.forEach(key => pushDiff(rows, { path: key, label: `未建模字段 · ${key}` }, left[key], right[key]));
    return rows;
  }

  function pushDiff(rows, field, before, after) {
    if (!same(before, after)) rows.push({ path: field.path, label: field.label, before, after });
  }

  function same(left, right) {
    return JSON.stringify(left) === JSON.stringify(right);
  }

  function renderDiff(before, after, schema) {
    const rows = diff(before, after, schema);
    if (!rows.length) return `<div class="empty-inline">当前没有字段变化。</div>`;
    return `<div class="editor-diff-summary">${rows.length} 个字段发生变化</div><div class="table-wrap"><table class="editor-diff-table"><thead><tr><th>字段</th><th>保存前</th><th>保存后</th></tr></thead><tbody>${rows.map(row => `<tr><td><strong>${esc(row.label)}</strong><div class="cell-sub mono">${esc(row.path)}</div></td><td class="editor-diff-before">${esc(formatValue(row.before))}</td><td class="editor-diff-after">${esc(formatValue(row.after))}</td></tr>`).join("")}</tbody></table></div>`;
  }

  function formatValue(value) {
    if (value === undefined) return "—";
    if (value === null) return "null";
    if (typeof value === "boolean") return value ? "是" : "否";
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  }

  function cssEscape(value) {
    return globalThis.CSS?.escape ? globalThis.CSS.escape(String(value)) : String(value).replaceAll('"', '\\"');
  }

  window.MirContentEditor = {
    addStatRow,
    defaultData,
    diff,
    readForm,
    renderDiff,
    renderForm
  };
})();
