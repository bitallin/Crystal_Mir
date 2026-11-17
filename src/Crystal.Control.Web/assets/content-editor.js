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
          setPath(data, `${field.path}.${child.path}`, clone(child.defaultValue));
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

  function renderField(field, data, errors, options, prefix = "") {
    const fullPath = prefix ? `${prefix}.${field.path}` : field.path;
    if (field.kind === "object" && Array.isArray(field.fields)) {
      return `<div class="editor-object"><div class="editor-object-head"><strong>${esc(field.label)}</strong><span>${field.fields.length} 个字段</span></div><div class="editor-object-grid">${field.fields.map(child => renderField(child, data, errors, options, fullPath)).join("")}</div></div>`;
    }
    if (field.kind === "stat-map") {
      return renderStats(field, data, errors);
    }
    if (field.kind === "drop-table") {
      return renderDropTable(field, data, errors, options);
    }

    const controlField = fullPath === field.path ? field : { ...field, path: fullPath };
    const value = getPath(data, fullPath);
    const error = (errors[fullPath] || [])[0];
    const disabled = options.readOnly || field.readOnly ? " disabled" : "";
    const required = field.required ? `<span class="editor-required">必填</span>` : "";
    let control = "";
    if (field.kind === "boolean") {
      control = `<label class="editor-check"><input class="checkbox" type="checkbox" data-edit-path="${esc(fullPath)}"${value ? " checked" : ""}${disabled}><span data-boolean-label>${value ? "已启用" : "未启用"}</span></label>`;
    } else if (field.kind === "enum") {
      control = `<select data-edit-path="${esc(fullPath)}"${disabled}>${(field.options || []).map(option => `<option value="${esc(option.value)}"${String(option.value) === String(value) ? " selected" : ""}>${esc(option.label)} · ${esc(option.value)}</option>`).join("")}</select>`;
    } else if (field.kind === "flags") {
      const selected = new Set(String(value || "None").split(",").map(item => item.trim()).filter(Boolean));
      control = `<div class="editor-flags">${(field.options || []).map(option => `<label class="editor-check"><input class="checkbox" type="checkbox" data-edit-flag-path="${esc(fullPath)}" value="${esc(option.value)}"${selected.has(option.value) ? " checked" : ""}${disabled}><span>${esc(option.label)}</span></label>`).join("")}</div>`;
    } else if (field.kind === "reference") {
      control = renderReference(controlField, value, disabled, options);
    } else if (field.kind === "reference-array") {
      control = renderReferenceArray(controlField, value, options, disabled);
    } else if (field.kind === "array") {
      control = renderObjectArray(controlField, value, errors, options, disabled);
    } else if (field.kind === "string-array" || field.kind === "integer-array") {
      const joined = field.kind === "integer-array"
        ? (Array.isArray(value) ? value.join(", ") : "")
        : (Array.isArray(value) ? value.join("\n") : "");
      control = `<textarea data-edit-path="${esc(fullPath)}" data-array-kind="${esc(field.kind)}" rows="${field.kind === "string-array" ? 4 : 2}"${disabled}>${esc(joined)}</textarea>`;
    } else if (field.kind === "color") {
      control = renderColor(controlField, value, disabled);
    } else if (field.kind === "integer") {
      const min = field.min === null || field.min === undefined ? "" : ` min="${esc(field.min)}"`;
      const max = field.max === null || field.max === undefined ? "" : ` max="${esc(field.max)}"`;
      const placeholder = field.readOnly && fullPath === "Index" ? ` placeholder="自动分配"` : "";
      control = `<input type="number" step="1" value="${value === null || value === undefined ? "" : esc(value)}" data-edit-path="${esc(fullPath)}"${min}${max}${placeholder}${disabled}>`;
    } else if (field.kind === "number") {
      const min = field.min === null || field.min === undefined ? "" : ` min="${esc(field.min)}"`;
      const max = field.max === null || field.max === undefined ? "" : ` max="${esc(field.max)}"`;
      control = `<input type="number" step="any" value="${value === null || value === undefined ? "" : esc(value)}" data-edit-path="${esc(fullPath)}"${min}${max}${disabled}>`;
    } else if (field.kind === "code") {
      control = `<textarea class="editor-code" data-edit-path="${esc(fullPath)}" rows="18" spellcheck="false"${disabled}>${esc(value ?? "")}</textarea>`;
    } else if (field.kind === "readonly") {
      control = renderReadonly(controlField, value);
    } else if (field.multiline) {
      control = `<textarea data-edit-path="${esc(fullPath)}" rows="4"${disabled}>${esc(value ?? "")}</textarea>`;
    } else {
      control = `<input type="text" value="${esc(value ?? "")}" data-edit-path="${esc(fullPath)}"${disabled}>`;
    }

    return `<div class="field editor-field${error ? " has-error" : ""}" data-field-path="${esc(fullPath)}"><label>${esc(field.label)}${required}</label>${control}<div class="editor-hint mono">${esc(fullPath)}</div>${error ? `<div class="editor-error">${esc(error)}</div>` : ""}</div>`;
  }

  function renderReadonly(field, value) {
    const text = value === undefined || value === null
      ? "—"
      : typeof value === "object" ? JSON.stringify(value, null, 2) : String(value);
    return `<pre class="editor-readonly-value" data-readonly-path="${esc(field.path)}">${esc(text)}</pre>`;
  }

  function renderColor(field, value, disabled) {
    const color = value && typeof value === "object" ? value : { a: 255, r: 255, g: 255, b: 255 };
    const hex = `#${[color.r, color.g, color.b].map(part => Math.max(0, Math.min(255, Number(part) || 0)).toString(16).padStart(2, "0")).join("")}`;
    return `<div class="editor-color"><input type="color" value="${hex}" data-edit-path="${esc(field.path)}" data-color-alpha="${esc(color.a ?? 255)}"${disabled}><input type="text" value="${esc(hex)}" data-color-hex readonly><span>A ${esc(color.a ?? 255)}</span></div>`;
  }

  function referenceKey(field) {
    return field.referenceType || "item";
  }

  function referenceList(field, options) {
    return (options.referenceOptions && options.referenceOptions[referenceKey(field)]) || [];
  }

  function referenceValue(field, value) {
    if (field.storeAs === "string") {
      if (value && typeof value === "object" && field.referenceType === "script" && typeof value.summary === "string") {
        return value.summary.replace(/^Envir\/NPCs\//i, "").replace(/\.txt$/i, "");
      }
      if (value && typeof value === "object") return String(value.label || value.contentKey || "");
      return value === undefined || value === null ? "" : String(value);
    }
    if (value && typeof value === "object") {
      const index = value.index === undefined ? value.localIndex : value.index;
      return index === undefined || index === null ? "" : String(index);
    }
    return value === undefined || value === null ? "" : String(value);
  }

  function referenceLabel(field, option) {
    return field.storeAs === "string"
      ? String(option.summary || option.label || option.contentKey || "")
      : `${option.label || option.contentKey} · ${option.contentKey}`;
  }

  function renderReference(field, value, disabled, options) {
    const current = referenceValue(field, value);
    const list = referenceList(field, options);
    const hasCurrent = list.some(option => String(referenceValue(field, option)) === current);
    const choices = list.map(option => `<option value="${esc(referenceValue(field, option))}" data-key="${esc(option.contentKey || "")}"${String(referenceValue(field, option)) === current ? " selected" : ""}>${esc(referenceLabel(field, option))}</option>`).join("");
    const fallback = current && !hasCurrent ? `<option value="${esc(current)}" selected>当前值 · ${esc(current)}</option>` : "";
    return `<div class="editor-reference-picker">${renderReferenceSearch(field, disabled)}<select data-edit-path="${esc(field.path)}" data-reference-type="${esc(referenceKey(field))}" data-reference-store-as="${esc(field.storeAs || "index")}"${disabled}><option value="">${field.nullable ? "无引用" : "请选择引用"}</option>${fallback}${choices}</select></div>`;
  }

  function renderReferenceArray(field, value, options, disabled) {
    const items = Array.isArray(value) ? value : [];
    const list = referenceList(field, options);
    const row = (selected, index) => `<div class="editor-reference-array-row" data-reference-array-row><div class="editor-reference-picker">${renderReferenceSearch(field, disabled)}<select data-reference-array-value data-reference-type="${esc(referenceKey(field))}" data-reference-store-as="${esc(field.storeAs || "index")}"><option value="">请选择引用</option>${list.map(option => `<option value="${esc(option.localIndex)}" data-key="${esc(option.contentKey || "")}"${String(option.localIndex) === String(selected) ? " selected" : ""}>${esc(referenceLabel(field, option))}</option>`).join("")}${selected && !list.some(option => String(option.localIndex) === String(selected)) ? `<option value="${esc(selected)}" selected>当前值 · ${esc(selected)}</option>` : ""}</select></div><button class="icon-btn" type="button" data-action="editor-remove-reference-array" title="移除引用"${disabled}>×</button></div>`;
    return `<div class="editor-reference-array" data-reference-array="${esc(field.path)}"><div class="editor-reference-array-rows">${items.map((item, index) => row(item, index)).join("")}</div><template class="editor-reference-array-template">${row("", "__INDEX__")}</template><button class="btn btn-ghost btn-sm" type="button" data-action="editor-add-reference-array"${disabled}>+ 添加引用</button></div>`;
  }

  function renderObjectArray(field, value, errors, options, disabled) {
    const items = Array.isArray(value) ? value : [];
    return `<div class="editor-object-array" data-editor-array="${esc(field.path)}"><div class="editor-array-rows">${items.map((item, index) => renderArrayRow(field, item, index, errors, options, disabled)).join("")}</div><template class="editor-array-template">${renderArrayRow(field, {}, "__INDEX__", {}, options, disabled)}</template><button class="btn btn-ghost btn-sm" type="button" data-action="editor-add-array"${disabled}>+ 添加${esc(field.label)}</button></div>`;
  }

  function renderArrayRow(field, item, index, errors, options, disabled) {
    const children = (field.fields || []).map(child => {
      const path = `${field.path}[${index}].${child.path}`;
      const error = (errors[path] || [])[0];
      return `<div class="field editor-field${error ? " has-error" : ""}"><label>${esc(child.label)}${child.required ? `<span class="editor-required">必填</span>` : ""}</label>${renderArrayChild(field, child, item?.[child.path], options, disabled)}${error ? `<div class="editor-error">${esc(error)}</div>` : ""}</div>`;
    }).join("");
    return `<div class="editor-array-row" data-array-row><div class="editor-array-row-head"><strong>#${esc(index === "__INDEX__" ? "新" : Number(index) + 1)}</strong><button class="icon-btn" type="button" data-action="editor-remove-array" title="移除条目"${disabled}>×</button></div><div class="editor-array-grid">${children}</div></div>`;
  }

  function renderArrayChild(field, child, value, options, disabled) {
    const base = `data-array-path="${esc(field.path)}" data-array-child="${esc(child.path)}"`;
    if (child.kind === "reference") {
      const list = referenceList(child, options);
      return `<div class="editor-reference-picker">${renderReferenceSearch(child, disabled)}<select ${base} data-array-reference data-reference-type="${esc(referenceKey(child))}" data-reference-store-as="${esc(child.storeAs || "index")}"><option value="">请选择引用</option>${list.map(option => `<option value="${esc(child.storeAs === "string" ? (option.label || option.contentKey) : option.localIndex)}" data-key="${esc(option.contentKey || "")}"${String(child.storeAs === "string" ? (option.label || option.contentKey) : option.localIndex) === referenceValue(child, value) ? " selected" : ""}>${esc(referenceLabel(child, option))}</option>`).join("")}${value && !list.some(option => String(child.storeAs === "string" ? (option.label || option.contentKey) : option.localIndex) === referenceValue(child, value)) ? `<option value="${esc(referenceValue(child, value))}" selected>当前值 · ${esc(referenceValue(child, value))}</option>` : ""}</select></div>`;
    }
    if (child.kind === "integer" || child.kind === "number") {
      const step = child.kind === "number" ? "any" : "1";
      return `<input type="number" step="${step}" ${base} value="${value === undefined || value === null ? "" : esc(value)}"${disabled}>`;
    }
    if (child.kind === "boolean") {
      return `<label class="editor-check"><input class="checkbox" type="checkbox" ${base}${value ? " checked" : ""}${disabled}><span>${value ? "已启用" : "未启用"}</span></label>`;
    }
    if (child.kind === "enum") {
      return `<select ${base}${disabled}>${(child.options || []).map(option => `<option value="${esc(option.value)}"${String(option.value) === String(value) ? " selected" : ""}>${esc(option.label)}</option>`).join("")}</select>`;
    }
    return `<input type="text" ${base} value="${esc(value ?? "")}"${disabled}>`;
  }

  function renderReferenceSearch(field, disabled) {
    return `<input class="editor-reference-search" type="search" placeholder="搜索 ${esc(referenceKey(field))}..." aria-label="搜索 ${esc(field.label)}" data-reference-search data-reference-type="${esc(referenceKey(field))}" data-reference-store-as="${esc(field.storeAs || "index")}"${disabled}>`;
  }

  function renderDropTable(field, data, errors, options) {
    const drops = getPath(data, field.path);
    const rows = Array.isArray(drops) ? drops : [];
    const error = (errors[field.path] || [])[0];
    return `<div class="editor-object editor-drop-table${error ? " has-error" : ""}" data-drop-table="${esc(field.path)}"><div class="editor-object-head"><strong>${esc(field.label)}</strong><span>${rows.length} 个条目 · 支持嵌套组</span></div><div class="editor-drop-rows">${rows.map(entry => renderDropRow(entry, options, 0)).join("")}</div><template class="editor-drop-template">${renderDropRow({}, options, 0)}</template><button class="btn btn-ghost btn-sm" type="button" data-action="editor-add-drop">+ 添加掉落条目</button>${error ? `<div class="editor-error">${esc(error)}</div>` : ""}</div>`;
  }

  function renderDropRow(entry, options, depth) {
    const maxDropDepth = 16;
    const canNest = depth < maxDropDepth;
    const itemValue = entry?.Item && typeof entry.Item === "object" ? entry.Item.index : "";
    const group = entry?.GroupedDrop || entry?.groupedDrop || {};
    const groupItems = Array.isArray(group.items) ? group.items : Array.isArray(group.Items) ? group.Items : [];
    const mode = entry?.GroupedDrop || entry?.groupedDrop ? "group" : Number(entry?.Gold) > 0 ? "gold" : "item";
    const itemOptions = (options.referenceOptions && options.referenceOptions.item) || [];
    const itemChoices = itemOptions.map(option => `<option value="${esc(option.localIndex)}" data-key="${esc(option.contentKey || "")}"${String(option.localIndex) === String(itemValue) ? " selected" : ""}>${esc(option.label || option.contentKey)} · ${esc(option.contentKey)}</option>`).join("");
    return `<div class="editor-drop-row" data-drop-row data-drop-depth="${depth}">
      <div class="editor-drop-row-head"><strong>${depth ? "嵌套组条目" : "掉落条目"} · 概率优先</strong><button class="icon-btn" type="button" data-action="editor-remove-drop" title="移除条目">×</button></div>
      <div class="editor-drop-grid">
        <label><span>概率</span><input type="number" min="0" step="1" data-drop-field="Chance" value="${esc(entry?.Chance ?? 0)}"></label>
        <label><span>类型</span><input type="number" min="0" max="255" step="1" data-drop-field="Type" value="${esc(entry?.Type ?? 0)}"></label>
        <label class="editor-check"><input class="checkbox" type="checkbox" data-drop-field="QuestRequired"${entry?.QuestRequired ? " checked" : ""}><span>任务限定</span></label>
        <label><span>目标</span><select data-drop-mode><option value="item"${mode === "item" ? " selected" : ""}>物品</option><option value="gold"${mode === "gold" ? " selected" : ""}>金币</option><option value="group"${mode === "group" ? " selected" : ""}>嵌套组</option></select></label>
      </div>
      <div class="editor-drop-pane${mode === "item" ? "" : " hidden"}" data-drop-pane="item"><label><span>物品引用</span></label><div class="editor-reference-picker">${renderReferenceSearch({ label: "掉落物品", referenceType: "item", storeAs: "index" }, false)}<select data-drop-item data-reference-type="item" data-reference-store-as="index"><option value="">请选择引用</option>${itemChoices}${itemValue && !itemOptions.some(option => String(option.localIndex) === String(itemValue)) ? `<option value="${esc(itemValue)}" selected>当前值 · ${esc(itemValue)}</option>` : ""}</select></div></div>
      <div class="editor-drop-pane${mode === "gold" ? "" : " hidden"}" data-drop-pane="gold"><label><span>金币数量</span><input type="number" min="0" step="1" data-drop-gold value="${esc(entry?.Gold ?? 0)}"></label></div>
      <div class="editor-drop-pane${mode === "group" ? "" : " hidden"}" data-drop-pane="group" data-drop-group><div class="editor-drop-group-flags"><label class="editor-check"><input class="checkbox" type="checkbox" data-drop-group-field="Random"${group.Random ? " checked" : ""}><span>随机选择</span></label><label class="editor-check"><input class="checkbox" type="checkbox" data-drop-group-field="First"${group.First ? " checked" : ""}><span>优先首项</span></label></div><div class="editor-drop-rows" data-drop-group-rows>${groupItems.map(item => renderDropRow(item, options, depth + 1)).join("")}</div>${canNest ? `<template class="editor-drop-template">${renderDropRow({}, options, depth + 1)}</template><button class="btn btn-ghost btn-sm" type="button" data-action="editor-add-drop">+ 添加组内条目</button>` : `<div class="editor-hint">已达到最大嵌套深度 16。</div>`}</div>
    </div>`;
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
        field.fields.forEach(child => readField(form, child, data, field.path));
      } else if (field.kind === "stat-map") {
        const container = form.querySelector(`[data-stat-map="${cssEscape(field.path)}"]`);
        const values = {};
        container?.querySelectorAll(".editor-stat-row").forEach(row => {
          const key = row.querySelector("[data-stat-key]")?.value;
          const number = Number(row.querySelector("[data-stat-value]")?.value);
          if (key && Number.isFinite(number) && number !== 0) values[key] = Math.trunc(number);
        });
        setPath(data, field.path, { Values: values });
      } else if (field.kind === "drop-table") {
        if (!field.readOnly) readDropTable(form, field, data);
      } else if (field.kind === "array") {
        if (!field.readOnly) readObjectArray(form, field, data);
      } else if (field.kind === "reference-array") {
        if (!field.readOnly) readReferenceArray(form, field, data);
      } else {
        readField(form, field, data);
      }
    });
    return data;
  }

  function readField(form, field, data, prefix = "") {
    const fullPath = prefix ? `${prefix}.${field.path}` : field.path;
    if (field.readOnly) return;
    if (field.kind === "flags") {
      const checked = Array.from(form.querySelectorAll(`[data-edit-flag-path="${cssEscape(fullPath)}"]:checked`))
        .map(input => input.value)
        .filter(value => value !== "None");
      setPath(data, fullPath, checked.length ? checked.join(", ") : "None");
      return;
    }

    if (field.kind === "reference") {
      const reference = form.querySelector(`[data-edit-path="${cssEscape(fullPath)}"]`);
      if (!reference) return;
      setPath(data, fullPath, readReferenceValue(field, reference));
      return;
    }
    if (field.kind === "reference-array" || field.kind === "array" || field.kind === "drop-table") return;

    const control = form.querySelector(`[data-edit-path="${cssEscape(fullPath)}"]`);
    if (!control) return;
    if (field.kind === "boolean") {
      setPath(data, fullPath, Boolean(control.checked));
    } else if (field.kind === "integer" || field.kind === "number") {
      const number = Number(control.value);
      setPath(data, fullPath, Number.isFinite(number) ? (field.kind === "integer" ? Math.trunc(number) : number) : 0);
    } else if (field.kind === "string-array") {
      setPath(data, fullPath, control.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean));
    } else if (field.kind === "integer-array") {
      const values = control.value.split(/[\s,]+/).filter(Boolean)
        .map(value => Math.trunc(Number(value)))
        .filter(value => Number.isFinite(value));
      setPath(data, fullPath, values);
    } else if (field.kind === "color") {
      const hex = String(control.value || "#ffffff").replace("#", "");
      const parsed = hex.length === 6 ? hex.match(/.{2}/g) : null;
      const alpha = Number(control.dataset.colorAlpha ?? 255);
      setPath(data, fullPath, parsed ? {
        a: Number.isFinite(alpha) ? Math.max(0, Math.min(255, Math.trunc(alpha))) : 255,
        r: Number.parseInt(parsed[0], 16) || 0,
        g: Number.parseInt(parsed[1], 16) || 0,
        b: Number.parseInt(parsed[2], 16) || 0
      } : { a: 255, r: 255, g: 255, b: 255 });
    } else {
      setPath(data, fullPath, control.value);
    }
  }

  function readReferenceValue(field, select) {
    const raw = select.value;
    if (raw === "") {
      if (field.storeAs === "string") return "";
      if (field.storeAs === "ref") return null;
      return 0;
    }

    const option = select.selectedOptions?.[0] || null;
    const key = option?.dataset?.key || select.dataset.key || "";
    if (field.storeAs === "string") return raw;
    if (field.storeAs === "ref" || field.kind === "reference" && field.storeAs === "ref") {
      return buildReferenceObject(field, raw, key);
    }
    return Math.trunc(Number(raw));
  }

  function buildReferenceObject(field, index, key) {
    const value = { $ref: field.referenceType || "item", index: Math.trunc(Number(index)) };
    if (key) value.key = key;
    return value;
  }

  function readReferenceArray(form, field, data) {
    const container = form.querySelector(`[data-reference-array="${cssEscape(field.path)}"]`);
    const values = [];
    container?.querySelectorAll("[data-reference-array-row] [data-reference-array-value]").forEach(select => {
      if (!select.value) return;
      if (field.storeAs === "string") {
        values.push(select.value);
      } else if (field.storeAs === "ref") {
        values.push(buildReferenceObject(field, select.value, select.selectedOptions?.[0]?.dataset?.key || ""));
      } else {
        const number = Math.trunc(Number(select.value));
        if (Number.isFinite(number)) values.push(number);
      }
    });
    setPath(data, field.path, values);
  }

  function readObjectArray(form, field, data) {
    const container = form.querySelector(`[data-editor-array="${cssEscape(field.path)}"]`);
    const rows = container?.querySelectorAll(":scope > .editor-array-rows > [data-array-row]") || [];
    const values = Array.from(rows).map(row => {
      const item = {};
      (field.fields || []).forEach(child => {
        if (child.readOnly) return;
        const control = row.querySelector(`[data-array-child="${cssEscape(child.path)}"]`);
        if (!control) return;
        if (child.kind === "boolean") {
          item[child.path] = Boolean(control.checked);
        } else if (child.kind === "integer" || child.kind === "number") {
          const number = Number(control.value);
          item[child.path] = Number.isFinite(number) ? (child.kind === "integer" ? Math.trunc(number) : number) : 0;
        } else if (child.kind === "reference") {
          if (control.value === "") {
            item[child.path] = child.storeAs === "string" ? "" : child.storeAs === "ref" ? null : 0;
          } else if (child.storeAs === "string") {
            item[child.path] = control.value;
          } else if (child.storeAs === "ref") {
            item[child.path] = buildReferenceObject(child, control.value, control.selectedOptions?.[0]?.dataset?.key || "");
          } else {
            item[child.path] = Math.trunc(Number(control.value));
          }
        } else {
          item[child.path] = control.value;
        }
      });
      return item;
    });
    setPath(data, field.path, values);
  }

  function readDropTable(form, field, data) {
    const container = form.querySelector(`[data-drop-table="${cssEscape(field.path)}"]`);
    if (!container) return;
    const rows = container.querySelector(":scope > .editor-drop-rows");
    setPath(data, field.path, readDropRows(rows));
  }

  function readDropRows(container) {
    if (!container) return [];
    return Array.from(container.querySelectorAll(":scope > [data-drop-row]")).map(row => {
      const entry = {};
      row.querySelectorAll(":scope > .editor-drop-grid [data-drop-field]").forEach(control => {
        const name = control.dataset.dropField;
        if (control.type === "checkbox") {
          entry[name] = Boolean(control.checked);
        } else {
          const number = Math.trunc(Number(control.value));
          entry[name] = Number.isFinite(number) ? number : 0;
        }
      });
      entry.Chance = Number.isFinite(entry.Chance) ? entry.Chance : 0;
      entry.Type = Number.isFinite(entry.Type) ? entry.Type : 0;
      entry.QuestRequired = Boolean(entry.QuestRequired);

      const mode = row.querySelector(":scope > .editor-drop-grid [data-drop-mode]")?.value || "item";
      if (mode === "gold") {
        const gold = Math.trunc(Number(row.querySelector(":scope > [data-drop-pane=\"gold\"] [data-drop-gold]")?.value));
        entry.Gold = Number.isFinite(gold) ? gold : 0;
      } else if (mode === "group") {
        const groupPane = row.querySelector(":scope > [data-drop-pane=\"group\"]");
        const random = Boolean(groupPane?.querySelector("[data-drop-group-field=\"Random\"]")?.checked);
        const first = Boolean(groupPane?.querySelector("[data-drop-group-field=\"First\"]")?.checked);
        const items = readDropRows(groupPane?.querySelector("[data-drop-group-rows]"));
        entry.GroupedDrop = { Random: random, First: first, items };
      } else {
        const select = row.querySelector(":scope > [data-drop-pane=\"item\"] [data-drop-item]");
        if (select?.value) {
          entry.Item = buildReferenceObject(
            { referenceType: "item" },
            select.value,
            select.selectedOptions?.[0]?.dataset?.key || "");
        }
      }
      return entry;
    });
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
        field.fields.forEach(child => {
          const childPath = `${field.path}.${child.path}`;
          pushDiff(rows, { ...child, path: childPath }, getPath(left, childPath), getPath(right, childPath));
        });
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
