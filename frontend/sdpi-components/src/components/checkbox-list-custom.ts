import { css, html, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { keyed } from 'lit/directives/keyed.js';

import { Checkable, DataSourced, DynamicValueType, Gridded, Input, Persisted } from '../mixins';


function getByPath(obj: any, path: string) {
    console.log("getByPath: path: " + path + ", from obj: ", obj);

    var parts = path.split('.').filter(element => element);
    var o = obj;

    if (o === undefined || o === null) {
        return [];
    }

    if (parts.length > 1) {
        for (var i = 0; i < parts.length - 1; i++) {
            if (!o[parts[i]])
                return [];
            o = o[parts[i]];
        }
    }

    return o[parts[parts.length - 1]] || []
}

function removeByPath(obj: any, path: string, value: string | number | boolean) {
    console.log("removeByPath: path: " + path + ", value: " + value + ", from obj: ", obj);
    var parts = path.split('.').filter(element => element);
    var o = obj;
    if (parts.length > 1) {
        for (var i = 0; i < parts.length - 1; i++) {
            if (!o[parts[i]])
                return;
            o = o[parts[i]];
        }
    }
    if (!o[parts[parts.length - 1]]) {
        console.log("no children")
        return;
    }
    o[parts[parts.length - 1]] = o[parts[parts.length - 1]].filter((element: string) => element != value);
    if (o[parts[parts.length - 1]].length == 0)
        delete o[parts[parts.length - 1]]
}

function setByPath(obj: any, path: string, value: string | number | boolean, exclusive = false) {
    console.log("setByPath: path: " + path + ", value: " + value + ", exclusive: " + exclusive + ", to obj: ", obj);

    var parts = path.split('.').filter(element => element);
    var o = obj;
    var last_part = parts.length - 1

    if (last_part > 0) {
        for (var i = 0; i < last_part; i++) {
            if (!o[parts[i]])
                o[parts[i]] = {};
            o = o[parts[i]];
        }
    }

    if (exclusive) {
        // Radio-like: only a single value is kept at the path; re-render unchecks siblings.
        o[parts[last_part]] = [value];
        console.log("values after exclusive set: ", o[parts[last_part]])
        return;
    }

    // Additively append to the array at the path, so multiple selections
    // under the same group are preserved (checkbox-list semantics).
    if (!Array.isArray(o[parts[last_part]])) {
        o[parts[last_part]] = [];
    }
    console.log("values before set: ", o[parts[last_part]])
    if (o[parts[last_part]].indexOf(value) === -1) {
        o[parts[last_part]].push(value);
    }
    console.log("values after set: ", o[parts[last_part]])
}

@customElement('sdpi-checkbox-list-custom')
export class CheckboxListCustom extends Gridded(Persisted(Checkable(DataSourced(DynamicValueType(Input<typeof LitElement, Array<boolean | number | string>>(LitElement)))))) {
    /**
     * When true, only a single value is kept per group path (radio-like behaviour);
     * checking an item replaces any previously selected sibling in the same group.
     */
    @property({ attribute: 'exclusive', type: Boolean })
    public exclusive = false;

    /** @inheritdoc */
    public static get styles() {
        return [
            ...super.styles,
            css`
                .loading {
                    margin: 0;
                    padding: calc(var(--spacer) * 1.5) 0;
                    user-select: none;
                }
            `
        ];
    }

    /**
     * Assigns dot-joined group paths (e.g. '.MODEL-A.Input Select') to every item,
     * based on the labels of the groups it is nested within. Root items get '.'.
     * Must run before rendering so both check-state lookup and change handling
     * can resolve the settings path of each item.
     */
    _assignGroupPaths(items?: any[], prefix = '.') {
        if (!items) {
            return;
        }
        for (const item of items) {
            if (item && Array.isArray(item.children)) {
                const label = item.label?.toString() ?? '';
                this._assignGroupPaths(item.children, prefix === '.' ? '.' + label : prefix + '.' + label);
            } else if (item) {
                item.groups = prefix;
            }
        }
    }

    _get_is_checked(item: any) {
        var path = item.groups || "."
        var search = getByPath(this, 'value' + path);
        console.log("::_get_is_checked - search: ", search);
        if (Array.isArray(search)) {
            return search.findIndex((v) => v == item.value) > -1;
        }
        return search == item.value;
    }



    /** @inheritdoc */
    protected render() {
        return this.items.render({
            pending: () => html`<p class="loading">${this.loadingText}</p>`,
            complete: () => {
                this._assignGroupPaths(this.items.value as any[]);
                return this.renderGrid(
                    this.renderDataSource((item) => {
                        console.log("rendering item: ", item);
                        return this.renderCheckable(
                            'checkbox',
                            keyed((item.groups || '.') + '=' + item.value,
                                html`
                                    <input
                                        type="checkbox"
                                        data-groups=${item.groups || '.'}
                                        .checked="${this._get_is_checked(item)}"
                                        .disabled=${this.disabled || item.disabled || false}
                                        .value=${item.value}
                                        @change=${this.handleChange}
                                />`
                            ),
                            item.label
                        )}
                    ,(group: any, children: any) => {
                        console.log("rendering group: ", group, " with children ", children)
                        var _a;
                        return html`<div class="group">${((_a = group.label) === null || _a === void 0 ? void 0 : _a.toString()) || ''}</div><hr/>${children}</div><br/>`;
                    })
                );
            }
        });
    }

    /**
     * Handles a checkbox state changing.
     * @param ev The event data.
     */
    private handleChange(ev: HTMLInputEvent<HTMLInputElement>): void {
        const value = this.parseValue(ev.target.value);
        if (value === undefined) {
            return;
        }

        // The settings path of the item is carried on the input's data-groups
        // attribute (assigned during render), e.g. '.MODEL-A.Input Select'.
        const grouping = ev.target.dataset?.groups || '.';
        var this_mock = {
            "value": JSON.parse(JSON.stringify(this.value ?? {})) as Object
        }
        console.log("Event target: ", ev.target, "dataset: ", ev.target.dataset, " grouping: ", grouping)

        if (ev.target.checked) {
            setByPath(this_mock, "value" + grouping, value, this.exclusive);
        }
        else {
            removeByPath(this_mock, "value" + grouping, value);
        }

        // removeByPath deletes keys whose arrays become empty; when the very last
        // selection is removed the root value disappears entirely. Fall back to the
        // empty shape matching the current root type so the setting stays valid
        // ([] for flat lists, {} for grouped ones) instead of crashing on JSON.parse.
        if (this_mock.value === undefined) {
            this_mock.value = Array.isArray(this.value) ? [] : {};
        }

        this.value = JSON.parse(JSON.stringify(this_mock.value)); // trigger valuechanged event
        console.log("Values: ", this.value);
        return;
    }
}

declare global {
    interface HTMLElementTagNameMap {
        'sdpi-checkbox-list-custom': CheckboxListCustom;
    }
}
